using backend.Dtos;
using backend.Exceptions;

namespace backend.Services.Orders;

internal static class OrderRequestParser
{
    public static IReadOnlyList<RequestedOrderItem> ParseItems(IReadOnlyList<CreateOrderItemRequest>? items)
    {
        if (items is null || items.Count == 0)
        {
            throw ApiException.BadRequest("Selecione pelo menos um produto.");
        }

        if (items.Any(item => item.SalePrice.HasValue && !IsValidSalePrice(item.SalePrice.Value)))
        {
            throw ApiException.BadRequest("O preco de venda do item deve ser maior que zero.");
        }

        var validItems = items.Where(item => item.Quantity > 0).ToList();
        if (validItems.Count == 0)
        {
            throw ApiException.BadRequest("Informe uma quantidade valida.");
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
                throw ApiException.BadRequest(
                    "Nao envie o mesmo produto com precos de venda diferentes no mesmo pedido.");
            }

            requestedItems.Add(new RequestedOrderItem(
                group.Key,
                group.Sum(item => item.Quantity),
                salePrices.Count == 1 ? salePrices[0] : null));
        }

        return requestedItems;
    }

    public static string? NormalizeCustomerName(string? customerName)
    {
        var normalized = NormalizeOptional(customerName);
        if (normalized?.Length > 120)
        {
            throw ApiException.BadRequest("O nome do cliente deve ter no maximo 120 caracteres.");
        }

        return normalized;
    }

    public static string? NormalizeObservations(string? observations)
    {
        var normalized = NormalizeOptional(observations);
        if (normalized?.Length > 1000)
        {
            throw ApiException.BadRequest("As observacoes devem ter no maximo 1000 caracteres.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsValidSalePrice(double salePrice) =>
        !double.IsNaN(salePrice) && !double.IsInfinity(salePrice) && salePrice > 0;
}

internal sealed record RequestedOrderItem(int ProductId, int Quantity, double? SalePrice);
