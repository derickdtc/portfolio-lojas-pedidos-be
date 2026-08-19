using backend.Dtos;
using backend.Models;

namespace backend.Services.Orders;

internal static class OrderMapper
{
    public static OrderResponse ToResponse(Order order)
    {
        var items = ToItems(order);
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

    public static OrderSummaryResponse ToSummaryResponse(Order order)
    {
        var items = ToItems(order);
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

    private static IReadOnlyList<OrderItemResponse> ToItems(Order order) =>
        order.Items
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
}
