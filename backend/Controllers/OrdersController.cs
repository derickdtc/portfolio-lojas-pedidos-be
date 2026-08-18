using System.Data;
using backend.Data;
using backend.Dtos;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class OrdersController(AppDbContext dbContext, ICurrentUserService currentUserService) : ControllerBase
{
    private const string CreatedStatus = "created";
    private const string EditingStatus = "editing";
    private const string DeletedStatus = "deleted";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderSummaryResponse>>> Get(
        [FromQuery] string? customerName,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? orderIds,
        CancellationToken cancellationToken)
    {
        if (!TryCreateOrderFilters(
                customerName,
                startDate,
                endDate,
                orderIds,
                out var filters,
                out var errorMessage))
        {
            return BadRequest(new { message = errorMessage });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var orders = await BuildFilteredOrdersQuery(storeId, filters)
            .Include(order => order.Items)
            .AsSplitQuery()
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .ToListAsync(cancellationToken);

        return Ok(orders.Select(ToSummaryResponse).ToList());
    }

    // Kept separate from GET /api/orders to retain the response contract consumed by
    // existing clients while enabling bounded list reads for new clients.
    [HttpGet("paged")]
    public async Task<ActionResult<PagedResponse<OrderSummaryResponse>>> GetPaged(
        [FromQuery] string? customerName,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? orderIds,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidatePagination(page, pageSize, out var paginationError))
        {
            return BadRequest(new { message = paginationError });
        }

        if (!TryCreateOrderFilters(
                customerName,
                startDate,
                endDate,
                orderIds,
                out var filters,
                out var filterError))
        {
            return BadRequest(new { message = filterError });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var query = BuildFilteredOrdersQuery(storeId, filters);
        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await query
            .Include(order => order.Items)
            .AsSplitQuery()
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<OrderSummaryResponse>(
            page,
            pageSize,
            totalCount,
            GetTotalPages(totalCount, pageSize),
            orders.Select(ToSummaryResponse).ToList()));
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var requestedItemsResult = ParseRequestedItems(request.Items);
        if (requestedItemsResult.ErrorMessage is not null)
        {
            return BadRequest(new { message = requestedItemsResult.ErrorMessage });
        }

        var requestedItems = requestedItemsResult.Items;
        var customerName = NormalizeCustomerName(request.CustomerName);
        if (customerName?.Length > 120)
        {
            return BadRequest(new { message = "O nome do cliente deve ter no maximo 120 caracteres." });
        }

        var observations = NormalizeObservations(request.Observations);
        if (observations?.Length > 1000)
        {
            return BadRequest(new { message = "As observacoes devem ter no maximo 1000 caracteres." });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var currentUserId = currentUserService.GetUserId();
        var currentUsername = currentUserService.GetUsername();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var productIds = requestedItems.Select(item => item.ProductId).ToArray();
        var products = await dbContext.Products
            .Where(product => product.StoreId == storeId)
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var order = new Order
        {
            StoreId = storeId,
            CreatedByUserId = currentUserId,
            CreatedByUsername = currentUsername,
            CustomerName = customerName,
            Observations = observations,
            CreatedAtUtc = DateTime.UtcNow,
        };

        foreach (var item in requestedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return BadRequest(new { message = "Um dos produtos selecionados nao esta disponivel." });
            }

            var salePrice = ResolveSalePrice(item, product);
            var lineTotal = item.Quantity * salePrice;
            product.StockBalance -= item.Quantity;
            product.UpdatedAtUtc = DateTime.UtcNow;

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductItemCode = product.ItemCode,
                ProductDescription = product.Description,
                ProductReference = product.Reference,
                Cfop = product.Cfop,
                Csosn = product.Csosn,
                Ncm = product.Ncm,
                Cst = product.Cst,
                Quantity = item.Quantity,
                SalePrice = salePrice,
                LineTotal = lineTotal,
            });

            order.TotalAmount += lineTotal;
        }

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToResponse(order));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<OrderResponse>> Update(
        int id,
        [FromBody] UpdateOrderRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Selecione pelo menos um produto." });
        }

        var requestedItemsResult = ParseRequestedItems(request.Items);
        if (requestedItemsResult.ErrorMessage is not null)
        {
            return BadRequest(new { message = requestedItemsResult.ErrorMessage });
        }

        var requestedItems = requestedItemsResult.Items;
        var customerName = NormalizeCustomerName(request.CustomerName);
        if (customerName?.Length > 120)
        {
            return BadRequest(new { message = "O nome do cliente deve ter no maximo 120 caracteres." });
        }

        var observations = NormalizeObservations(request.Observations);
        if (observations?.Length > 1000)
        {
            return BadRequest(new { message = "As observacoes devem ter no maximo 1000 caracteres." });
        }

        var storeId = currentUserService.GetCurrentStoreId();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var order = await dbContext.Orders
            .Include(existingOrder => existingOrder.Items)
            .FirstOrDefaultAsync(existingOrder =>
                existingOrder.Id == id
                && existingOrder.StoreId == storeId,
                cancellationToken);

        if (order is null)
        {
            return NotFound(new { message = "Pedido nao encontrado." });
        }

        if (!CanChangeOrder(order))
        {
            return BadRequest(new { message = "Este pedido nao pode mais ser editado." });
        }

        var requestedProductIds = requestedItems
            .Select(item => item.ProductId)
            .Distinct()
            .ToArray();
        var existingProductIds = order.Items
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToArray();
        var existingProductItemCodes = order.Items
            .Select(item => item.ProductItemCode)
            .Where(itemCode => !string.IsNullOrWhiteSpace(itemCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var productIds = requestedProductIds
            .Concat(existingProductIds)
            .Distinct()
            .ToArray();

        var products = await dbContext.Products
            .Where(product => product.StoreId == storeId)
            .Where(product => productIds.Contains(product.Id) || existingProductItemCodes.Contains(product.ItemCode))
            .ToListAsync(cancellationToken);
        var productsById = products.ToDictionary(product => product.Id);
        var productsByItemCode = products
            .GroupBy(product => product.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var missingProductIds = requestedProductIds
            .Where(productId => !productsById.ContainsKey(productId))
            .OrderBy(productId => productId)
            .ToList();

        if (missingProductIds.Count > 0)
        {
            return BadRequest(new
            {
                message = "Um ou mais produtos selecionados nao existem.",
                productIds = missingProductIds,
            });
        }

        var existingQuantitiesResult = ResolveExistingQuantities(order.Items, productsById, productsByItemCode);
        if (existingQuantitiesResult.ErrorMessage is not null)
        {
            return BadRequest(new { message = existingQuantitiesResult.ErrorMessage });
        }

        var requestedQuantities = requestedItems.ToDictionary(item => item.ProductId, item => item.Quantity);
        var productIdsToCompare = existingQuantitiesResult.Quantities.Keys
            .Concat(requestedQuantities.Keys)
            .Distinct()
            .ToList();
        var stockDeltas = new Dictionary<int, int>();

        foreach (var productId in productIdsToCompare)
        {
            var oldQuantity = existingQuantitiesResult.Quantities.GetValueOrDefault(productId);
            var newQuantity = requestedQuantities.GetValueOrDefault(productId);
            var delta = newQuantity - oldQuantity;

            if (delta == 0)
            {
                continue;
            }

            if (!productsById.TryGetValue(productId, out var product))
            {
                return BadRequest(new { message = $"Produto {productId} nao encontrado para ajustar estoque." });
            }

            if (delta > 0 && product.StockBalance < delta)
            {
                return BadRequest(new
                {
                    message = $"Estoque insuficiente para {product.Description}. Disponivel: {product.StockBalance}. Necessario adicional: {delta}.",
                });
            }

            stockDeltas[productId] = delta;
        }

        var now = DateTime.UtcNow;

        foreach (var (productId, delta) in stockDeltas)
        {
            var product = productsById[productId];
            product.StockBalance -= delta;
            product.UpdatedAtUtc = now;
        }

        dbContext.OrderItems.RemoveRange(order.Items);
        order.Items.Clear();
        order.CustomerName = customerName;
        order.Observations = observations;
        order.TotalAmount = 0;

        foreach (var item in requestedItems)
        {
            var product = productsById[item.ProductId];
            var orderItem = CreateOrderItem(product, item.Quantity, item.SalePrice);

            order.Items.Add(orderItem);
            order.TotalAmount += orderItem.LineTotal;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(order));
    }

    [HttpPost("{id:int}/edit")]
    public async Task<ActionResult<OrderResponse>> StartEdit(int id, CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var order = await dbContext.Orders
            .Include(existingOrder => existingOrder.Items)
            .FirstOrDefaultAsync(existingOrder =>
                existingOrder.Id == id
                && existingOrder.StoreId == storeId,
                cancellationToken);

        if (order is null)
        {
            return NotFound(new { message = "Pedido nao encontrado." });
        }

        if (!CanChangeOrder(order))
        {
            return BadRequest(new { message = "Este pedido nao pode mais ser editado." });
        }

        var restoreError = await RestoreStockAsync(order.Items, storeId, cancellationToken);
        if (restoreError is not null)
        {
            return BadRequest(new { message = restoreError });
        }

        order.Status = EditingStatus;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(order));
    }

    [HttpDelete]
    public async Task<ActionResult<DeleteOrdersResponse>> Delete(
        [FromBody] DeleteOrdersRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.OrderIds is null || request.OrderIds.Count == 0)
        {
            return BadRequest(new { message = "Informe pelo menos um pedido para excluir." });
        }

        var orderIds = request.OrderIds
            .Where(orderId => orderId > 0)
            .Distinct()
            .ToList();

        if (orderIds.Count != request.OrderIds.Count)
        {
            return BadRequest(new { message = "A lista de pedidos contem ids invalidos ou repetidos." });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var storeId = currentUserService.GetCurrentStoreId();
        var orders = await dbContext.Orders
            .Include(order => order.Items)
            .Where(order => order.StoreId == storeId)
            .Where(order => orderIds.Contains(order.Id))
            .ToListAsync(cancellationToken);

        if (orders.Count != orderIds.Count)
        {
            return NotFound(new { message = "Um ou mais pedidos nao foram encontrados." });
        }

        var blockedOrders = orders
            .Where(order => !CanChangeOrder(order))
            .Select(order => order.Id)
            .OrderBy(orderId => orderId)
            .ToList();

        if (blockedOrders.Count > 0)
        {
            return BadRequest(new
            {
                message = "Um ou mais pedidos nao podem ser excluidos.",
                orderIds = blockedOrders,
            });
        }

        var restoreError = await RestoreStockAsync(orders.SelectMany(order => order.Items), storeId, cancellationToken);
        if (restoreError is not null)
        {
            return BadRequest(new { message = restoreError });
        }

        foreach (var order in orders)
        {
            order.Status = DeletedStatus;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new DeleteOrdersResponse(orderIds.Count, orderIds));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(existingOrder => existingOrder.Items)
            .FirstOrDefaultAsync(existingOrder =>
                existingOrder.Id == id
                && existingOrder.StoreId == storeId,
                cancellationToken);

        return order is null ? NotFound() : Ok(ToResponse(order));
    }

    private async Task<string?> RestoreStockAsync(
        IEnumerable<OrderItem> orderItems,
        int storeId,
        CancellationToken cancellationToken)
    {
        var items = orderItems.ToList();
        if (items.Count == 0)
        {
            return null;
        }

        var productIds = items
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToArray();
        var productItemCodes = items
            .Select(item => item.ProductItemCode)
            .Where(itemCode => !string.IsNullOrWhiteSpace(itemCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var products = await dbContext.Products
            .Where(product => product.StoreId == storeId)
            .Where(product => productIds.Contains(product.Id) || productItemCodes.Contains(product.ItemCode))
            .ToListAsync(cancellationToken);
        var productsById = products.ToDictionary(product => product.Id);
        var productsByItemCode = products
            .GroupBy(product => product.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;

        foreach (var item in items)
        {
            Product? product = null;

            if (item.ProductId.HasValue)
            {
                productsById.TryGetValue(item.ProductId.Value, out product);
            }

            if (product is null
                && !string.IsNullOrWhiteSpace(item.ProductItemCode)
                && productsByItemCode.TryGetValue(item.ProductItemCode, out var productByCode))
            {
                product = productByCode;
                item.ProductId = product.Id;
            }

            if (product is null)
            {
                return $"Nao foi possivel devolver ao estoque o produto {item.ProductItemCode}.";
            }

            product.StockBalance += item.Quantity;
            product.UpdatedAtUtc = now;
        }

        return null;
    }

    private static ResolvedExistingQuantities ResolveExistingQuantities(
        IEnumerable<OrderItem> orderItems,
        IReadOnlyDictionary<int, Product> productsById,
        IReadOnlyDictionary<string, Product> productsByItemCode)
    {
        var quantities = new Dictionary<int, int>();

        foreach (var item in orderItems)
        {
            Product? product = null;

            if (item.ProductId.HasValue)
            {
                productsById.TryGetValue(item.ProductId.Value, out product);
            }

            if (product is null
                && !string.IsNullOrWhiteSpace(item.ProductItemCode)
                && productsByItemCode.TryGetValue(item.ProductItemCode, out var productByCode))
            {
                product = productByCode;
                item.ProductId = product.Id;
            }

            if (product is null)
            {
                return new ResolvedExistingQuantities(
                    new Dictionary<int, int>(),
                    $"Nao foi possivel localizar o produto antigo {item.ProductItemCode} para editar o pedido.");
            }

            quantities[product.Id] = quantities.GetValueOrDefault(product.Id) + item.Quantity;
        }

        return new ResolvedExistingQuantities(quantities, null);
    }

    private static OrderItem CreateOrderItem(Product product, int quantity, double? salePriceOverride)
    {
        var salePrice = salePriceOverride ?? product.SalePrice;
        var lineTotal = quantity * salePrice;

        return new OrderItem
        {
            ProductId = product.Id,
            ProductItemCode = product.ItemCode,
            ProductDescription = product.Description,
            ProductReference = product.Reference,
            Cfop = product.Cfop,
            Csosn = product.Csosn,
            Ncm = product.Ncm,
            Cst = product.Cst,
            Quantity = quantity,
            SalePrice = salePrice,
            LineTotal = lineTotal,
        };
    }

    private static bool CanChangeOrder(Order order)
    {
        return string.Equals(order.Status, CreatedStatus, StringComparison.OrdinalIgnoreCase);
    }

    private IQueryable<Order> BuildFilteredOrdersQuery(int storeId, OrderFilters filters)
    {
        var query = dbContext.Orders
            .AsNoTracking()
            .Where(order => order.StoreId == storeId)
            .Where(order => order.Status == CreatedStatus);

        if (!filters.HasAny)
        {
            return query;
        }

        return query.Where(order =>
            (filters.CustomerNamePattern != null
                && order.CustomerName != null
                && EF.Functions.ILike(order.CustomerName, filters.CustomerNamePattern))
            || (filters.OrderIds.Length > 0 && filters.OrderIds.Contains(order.Id))
            || (filters.HasDateFilter
                && (!filters.StartDateUtc.HasValue || order.CreatedAtUtc >= filters.StartDateUtc.Value)
                && (!filters.EndDateExclusiveUtc.HasValue || order.CreatedAtUtc < filters.EndDateExclusiveUtc.Value)));
    }

    private static bool TryCreateOrderFilters(
        string? customerName,
        DateTime? startDate,
        DateTime? endDate,
        string? orderIds,
        out OrderFilters filters,
        out string? errorMessage)
    {
        var parsedOrderIds = ParseOrderIds(orderIds);
        if (parsedOrderIds.ErrorMessage is not null)
        {
            filters = default!;
            errorMessage = parsedOrderIds.ErrorMessage;
            return false;
        }

        var normalizedCustomerName = NormalizeCustomerName(customerName);
        var startDateUtc = startDate.HasValue ? ToStartOfUtcDay(startDate.Value) : (DateTime?)null;
        var endDateExclusiveUtc = endDate.HasValue ? ToExclusiveEndOfUtcDay(endDate.Value) : (DateTime?)null;

        if (startDateUtc.HasValue
            && endDateExclusiveUtc.HasValue
            && startDateUtc.Value >= endDateExclusiveUtc.Value)
        {
            filters = default!;
            errorMessage = "A data inicial deve ser menor ou igual a data final.";
            return false;
        }

        filters = new OrderFilters(
            normalizedCustomerName is null ? null : $"%{normalizedCustomerName}%",
            parsedOrderIds.OrderIds.ToArray(),
            startDateUtc,
            endDateExclusiveUtc);
        errorMessage = null;
        return true;
    }

    private static bool TryValidatePagination(int page, int pageSize, out string? errorMessage)
    {
        if (page < 1)
        {
            errorMessage = "O parametro page deve ser maior ou igual a 1.";
            return false;
        }

        if (pageSize is < 1 or > 100)
        {
            errorMessage = "O parametro pageSize deve estar entre 1 e 100.";
            return false;
        }

        if (page > (int.MaxValue / pageSize) + 1)
        {
            errorMessage = "A combinacao de page e pageSize e muito grande.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private static int GetTotalPages(int totalCount, int pageSize)
    {
        return totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    private static ParsedOrderIds ParseOrderIds(string? orderIds)
    {
        if (string.IsNullOrWhiteSpace(orderIds))
        {
            return new ParsedOrderIds([], null);
        }

        var parsedOrderIds = new List<int>();
        var parts = orderIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var orderId) || orderId <= 0)
            {
                return new ParsedOrderIds([], "A lista orderIds deve conter apenas numeros positivos separados por virgula.");
            }

            parsedOrderIds.Add(orderId);
        }

        return new ParsedOrderIds(parsedOrderIds.Distinct().ToList(), null);
    }

    private static DateTime ToStartOfUtcDay(DateTime date)
    {
        return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
    }

    private static DateTime ToExclusiveEndOfUtcDay(DateTime date)
    {
        return DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Utc);
    }

    private static OrderResponse ToResponse(Order order)
    {
        var items = order.Items
            .OrderBy(item => item.ProductDescription)
            .Select(item => new OrderItemResponse(
                item.ProductId,
                item.ProductItemCode,
                item.ProductDescription,
                item.ProductReference,
                item.Cfop,
                item.Csosn,
                item.Ncm,
                item.Cst,
                item.Quantity,
                item.SalePrice,
                item.LineTotal))
            .ToList();

        return new OrderResponse(
            order.Id,
            order.CreatedAtUtc,
            order.CreatedByUsername,
            order.CustomerName,
            order.Observations,
            order.Status,
            order.TotalAmount,
            items.Sum(item => item.Quantity),
            items);
    }

    private static OrderSummaryResponse ToSummaryResponse(Order order)
    {
        var items = order.Items
            .OrderBy(item => item.ProductDescription)
            .Select(item => new OrderItemResponse(
                item.ProductId,
                item.ProductItemCode,
                item.ProductDescription,
                item.ProductReference,
                item.Cfop,
                item.Csosn,
                item.Ncm,
                item.Cst,
                item.Quantity,
                item.SalePrice,
                item.LineTotal))
            .ToList();

        return new OrderSummaryResponse(
            order.Id,
            order.CreatedAtUtc,
            order.CreatedByUsername,
            order.CustomerName,
            order.Observations,
            order.Status,
            order.TotalAmount,
            items.Sum(item => item.Quantity),
            items);
    }

    private static string? NormalizeCustomerName(string? customerName)
    {
        var normalized = customerName?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeObservations(string? observations)
    {
        var normalized = observations?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static ParsedRequestedItems ParseRequestedItems(IReadOnlyList<CreateOrderItemRequest>? items)
    {
        if (items is null || items.Count == 0)
        {
            return new ParsedRequestedItems([], "Selecione pelo menos um produto.");
        }

        foreach (var item in items)
        {
            if (item.SalePrice.HasValue && !IsValidSalePrice(item.SalePrice.Value))
            {
                return new ParsedRequestedItems([], "O preco de venda do item deve ser maior que zero.");
            }
        }

        var validItems = items
            .Where(item => item.Quantity > 0)
            .ToList();

        if (validItems.Count == 0)
        {
            return new ParsedRequestedItems([], "Informe uma quantidade valida.");
        }

        var requestedItems = new List<RequestedOrderItem>();

        foreach (var group in validItems.GroupBy(item => item.ProductId))
        {
            var salePrices = group
                .Where(item => item.SalePrice.HasValue)
                .Select(item => item.SalePrice.GetValueOrDefault())
                .Distinct()
                .ToList();

            if (salePrices.Count > 1)
            {
                return new ParsedRequestedItems(
                    [],
                    "Nao envie o mesmo produto com precos de venda diferentes no mesmo pedido.");
            }

            requestedItems.Add(new RequestedOrderItem(
                group.Key,
                group.Sum(item => item.Quantity),
                salePrices.Count == 1 ? salePrices[0] : null));
        }

        return new ParsedRequestedItems(requestedItems, null);
    }

    private static double ResolveSalePrice(RequestedOrderItem item, Product product)
    {
        return item.SalePrice ?? product.SalePrice;
    }

    private static bool IsValidSalePrice(double salePrice)
    {
        return !double.IsNaN(salePrice)
            && !double.IsInfinity(salePrice)
            && salePrice > 0;
    }

    private sealed record ParsedOrderIds(IReadOnlyList<int> OrderIds, string? ErrorMessage);

    private sealed record OrderFilters(
        string? CustomerNamePattern,
        int[] OrderIds,
        DateTime? StartDateUtc,
        DateTime? EndDateExclusiveUtc)
    {
        public bool HasDateFilter => StartDateUtc.HasValue || EndDateExclusiveUtc.HasValue;

        public bool HasAny => CustomerNamePattern is not null || OrderIds.Length > 0 || HasDateFilter;
    }

    private sealed record RequestedOrderItem(int ProductId, int Quantity, double? SalePrice);

    private sealed record ParsedRequestedItems(
        IReadOnlyList<RequestedOrderItem> Items,
        string? ErrorMessage);

    private sealed record ResolvedExistingQuantities(
        IReadOnlyDictionary<int, int> Quantities,
        string? ErrorMessage);
}
