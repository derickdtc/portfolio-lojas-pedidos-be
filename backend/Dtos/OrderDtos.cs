namespace backend.Dtos;

public sealed record CreateOrderRequest(
    string? CustomerName,
    string? Observations,
    IReadOnlyList<CreateOrderItemRequest> Items);

public sealed record UpdateOrderRequest(
    string? CustomerName,
    string? Observations,
    IReadOnlyList<CreateOrderItemRequest> Items);

public sealed record CreateOrderItemRequest(int ProductId, int Quantity, double? SalePrice = null);

public sealed record DeleteOrdersRequest(IReadOnlyList<int> OrderIds);

public sealed record DeleteOrdersResponse(int Deleted, IReadOnlyList<int> OrderIds);

public sealed record OrderResponse(
    int Id,
    DateTime CreatedAtUtc,
    string CreatedByUsername,
    string? CustomerName,
    string? Observations,
    string Status,
    double TotalAmount,
    int ItemsCount,
    IReadOnlyList<OrderItemResponse> Items);

public sealed record OrderSummaryResponse(
    int Id,
    DateTime CreatedAtUtc,
    string CreatedByUsername,
    string? CustomerName,
    string? Observations,
    string Status,
    double TotalAmount,
    int ItemsCount,
    IReadOnlyList<OrderItemResponse> Items);

public sealed record OrderItemResponse(
    int? ProductId,
    string ProductItemCode,
    string ProductDescription,
    string ProductReference,
    string Cfop,
    string Csosn,
    string Ncm,
    string Cst,
    int Quantity,
    double SalePrice,
    double LineTotal);
