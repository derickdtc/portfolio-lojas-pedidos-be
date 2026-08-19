using System.Data;
using backend.Data;
using backend.Dtos;
using backend.Exceptions;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Orders;

public sealed class OrderService(
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    OrderStockService orderStockService)
{
    private const string CreatedStatus = "created";
    private const string EditingStatus = "editing";
    private const string DeletedStatus = "deleted";

    public async Task<OrderResponse> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var requestedItems = OrderRequestParser.ParseItems(request.Items);
        var customerName = OrderRequestParser.NormalizeCustomerName(request.CustomerName);
        var observations = OrderRequestParser.NormalizeObservations(request.Observations);
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
                throw ApiException.BadRequest("Um dos produtos selecionados nao esta disponivel.");
            }

            var orderItem = OrderStockService.CreateOrderItem(product, item.Quantity, item.SalePrice);
            product.StockBalance -= item.Quantity;
            product.UpdatedAtUtc = DateTime.UtcNow;
            order.Items.Add(orderItem);
            order.TotalAmount += orderItem.LineTotal;
        }

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OrderMapper.ToResponse(order);
    }

    public async Task<OrderResponse> UpdateAsync(
        int id,
        UpdateOrderRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("Selecione pelo menos um produto.");
        }

        var requestedItems = OrderRequestParser.ParseItems(request.Items);
        var customerName = OrderRequestParser.NormalizeCustomerName(request.CustomerName);
        var observations = OrderRequestParser.NormalizeObservations(request.Observations);
        var storeId = currentUserService.GetCurrentStoreId();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var order = await dbContext.Orders
            .Include(existingOrder => existingOrder.Items)
            .FirstOrDefaultAsync(
                existingOrder => existingOrder.Id == id && existingOrder.StoreId == storeId,
                cancellationToken)
            ?? throw ApiException.NotFound("Pedido nao encontrado.");

        EnsureCanChange(order);

        var requestedProductIds = requestedItems.Select(item => item.ProductId).Distinct().ToArray();
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
        var productIds = requestedProductIds.Concat(existingProductIds).Distinct().ToArray();
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
            throw new ApiException(StatusCodes.Status400BadRequest, new
            {
                message = "Um ou mais produtos selecionados nao existem.",
                productIds = missingProductIds,
            });
        }

        var existingQuantities = orderStockService.ResolveExistingQuantities(
            order.Items,
            productsById,
            productsByItemCode);
        if (existingQuantities.ErrorMessage is not null)
        {
            throw ApiException.BadRequest(existingQuantities.ErrorMessage);
        }

        ApplyStockDeltas(requestedItems, existingQuantities.Quantities, productsById);
        dbContext.OrderItems.RemoveRange(order.Items);
        order.Items.Clear();
        order.CustomerName = customerName;
        order.Observations = observations;
        order.TotalAmount = 0;

        foreach (var item in requestedItems)
        {
            var orderItem = OrderStockService.CreateOrderItem(
                productsById[item.ProductId],
                item.Quantity,
                item.SalePrice);
            order.Items.Add(orderItem);
            order.TotalAmount += orderItem.LineTotal;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OrderMapper.ToResponse(order);
    }

    public async Task<OrderResponse> StartEditAsync(int id, CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var order = await dbContext.Orders
            .Include(existingOrder => existingOrder.Items)
            .FirstOrDefaultAsync(
                existingOrder => existingOrder.Id == id && existingOrder.StoreId == storeId,
                cancellationToken)
            ?? throw ApiException.NotFound("Pedido nao encontrado.");

        EnsureCanChange(order);
        var restoreError = await orderStockService.RestoreAsync(order.Items, storeId, cancellationToken);
        if (restoreError is not null)
        {
            throw ApiException.BadRequest(restoreError);
        }

        order.Status = EditingStatus;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OrderMapper.ToResponse(order);
    }

    public async Task<DeleteOrdersResponse> DeleteAsync(
        DeleteOrdersRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.OrderIds is null || request.OrderIds.Count == 0)
        {
            throw ApiException.BadRequest("Informe pelo menos um pedido para excluir.");
        }

        var orderIds = request.OrderIds.Where(orderId => orderId > 0).Distinct().ToList();
        if (orderIds.Count != request.OrderIds.Count)
        {
            throw ApiException.BadRequest("A lista de pedidos contem ids invalidos ou repetidos.");
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
            throw ApiException.NotFound("Um ou mais pedidos nao foram encontrados.");
        }

        var blockedOrders = orders
            .Where(order => !CanChange(order))
            .Select(order => order.Id)
            .OrderBy(orderId => orderId)
            .ToList();
        if (blockedOrders.Count > 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, new
            {
                message = "Um ou mais pedidos nao podem ser excluidos.",
                orderIds = blockedOrders,
            });
        }

        var restoreError = await orderStockService.RestoreAsync(
            orders.SelectMany(order => order.Items),
            storeId,
            cancellationToken);
        if (restoreError is not null)
        {
            throw ApiException.BadRequest(restoreError);
        }

        foreach (var order in orders)
        {
            order.Status = DeletedStatus;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DeleteOrdersResponse(orderIds.Count, orderIds);
    }

    private static void ApplyStockDeltas(
        IReadOnlyList<RequestedOrderItem> requestedItems,
        IReadOnlyDictionary<int, int> existingQuantities,
        IReadOnlyDictionary<int, Product> productsById)
    {
        var requestedQuantities = requestedItems.ToDictionary(item => item.ProductId, item => item.Quantity);
        var productIds = existingQuantities.Keys.Concat(requestedQuantities.Keys).Distinct();
        var stockDeltas = new Dictionary<int, int>();

        foreach (var productId in productIds)
        {
            var delta = requestedQuantities.GetValueOrDefault(productId)
                - existingQuantities.GetValueOrDefault(productId);
            if (delta == 0)
            {
                continue;
            }

            if (!productsById.TryGetValue(productId, out var product))
            {
                throw ApiException.BadRequest($"Produto {productId} nao encontrado para ajustar estoque.");
            }

            if (delta > 0 && product.StockBalance < delta)
            {
                throw ApiException.BadRequest(
                    $"Estoque insuficiente para {product.Description}. Disponivel: {product.StockBalance}. Necessario adicional: {delta}.");
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
    }

    private static void EnsureCanChange(Order order)
    {
        if (!CanChange(order))
        {
            throw ApiException.BadRequest("Este pedido nao pode mais ser editado.");
        }
    }

    private static bool CanChange(Order order) =>
        string.Equals(order.Status, CreatedStatus, StringComparison.OrdinalIgnoreCase);
}
