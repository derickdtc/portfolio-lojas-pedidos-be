namespace backend.Dtos;

public sealed record ProductStockResponse(
    int Id,
    string ItemCode,
    string Description,
    double PurchasePrice,
    double SalePrice,
    int StockBalance,
    string Cfop,
    string Csosn,
    string Ncm,
    string Cst,
    string Reference,
    string? ImageUrl1,
    string? ImageKey1,
    string? ImageUrl2,
    string? ImageKey2);

public sealed record ProductImportResponse(
    int Imported,
    int Replaced,
    int Skipped,
    IReadOnlyList<string> Warnings);

public sealed record CreateProductRequest(
    string? ItemCode,
    string? Description,
    double PurchasePrice,
    double SalePrice,
    int StockBalance,
    string? Cfop,
    string? Csosn,
    string? Ncm,
    string? Cst,
    string? Reference,
    string? ImageUrl1,
    string? ImageKey1,
    string? ImageUrl2,
    string? ImageKey2);

public sealed record UpdateProductRequest(
    string? ItemCode,
    string? Description,
    double? PurchasePrice,
    double? SalePrice,
    int? StockBalance,
    string? Cfop,
    string? Csosn,
    string? Ncm,
    string? Cst,
    string? Reference,
    string? ImageUrl1,
    string? ImageKey1,
    string? ImageUrl2,
    string? ImageKey2);
