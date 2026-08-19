using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Orders;

public sealed class OrderStockService(AppDbContext dbContext)
{
    public async Task<string?> RestoreAsync(
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
            var product = ResolveProduct(item, productsById, productsByItemCode);
            if (product is null)
            {
                return $"Nao foi possivel devolver ao estoque o produto {item.ProductItemCode}.";
            }

            product.StockBalance += item.Quantity;
            product.UpdatedAtUtc = now;
        }

        return null;
    }

    public ResolvedExistingQuantities ResolveExistingQuantities(
        IEnumerable<OrderItem> orderItems,
        IReadOnlyDictionary<int, Product> productsById,
        IReadOnlyDictionary<string, Product> productsByItemCode)
    {
        var quantities = new Dictionary<int, int>();
        foreach (var item in orderItems)
        {
            var product = ResolveProduct(item, productsById, productsByItemCode);
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

    public static OrderItem CreateOrderItem(Product product, int quantity, double? salePriceOverride)
    {
        var salePrice = salePriceOverride ?? product.SalePrice;
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
            LineTotal = quantity * salePrice,
        };
    }

    private static Product? ResolveProduct(
        OrderItem item,
        IReadOnlyDictionary<int, Product> productsById,
        IReadOnlyDictionary<string, Product> productsByItemCode)
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

        return product;
    }
}

public sealed record ResolvedExistingQuantities(
    IReadOnlyDictionary<int, int> Quantities,
    string? ErrorMessage);
