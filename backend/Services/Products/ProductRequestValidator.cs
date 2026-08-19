using backend.Dtos;

namespace backend.Services.Products;

internal static class ProductRequestValidator
{
    public static string? Validate(CreateProductRequest request) =>
        ValidateRequired(request.ItemCode, "codigo", 80)
        ?? ValidateRequired(request.Description, "descricao", 260)
        ?? ValidatePrice(request.PurchasePrice, "preco de compra")
        ?? ValidatePrice(request.SalePrice, "preco de venda")
        ?? ValidateStockBalance(request.StockBalance)
        ?? ValidateRequired(request.Cfop, "CFOP", 20)
        ?? ValidateRequired(request.Csosn, "CSOSN", 20)
        ?? ValidateRequired(request.Ncm, "NCM", 30)
        ?? ValidateRequired(request.Cst, "CST", 20)
        ?? ValidateRequired(request.Reference, "referencia", 80)
        ?? ValidateOptionalLength(request.ImageUrl1, "URL da imagem 1", 2048)
        ?? ValidateOptionalLength(request.ImageKey1, "chave da imagem 1", 512)
        ?? ValidateOptionalLength(request.ImageUrl2, "URL da imagem 2", 2048)
        ?? ValidateOptionalLength(request.ImageKey2, "chave da imagem 2", 512);

    public static string? Validate(UpdateProductRequest request) =>
        ValidateOptionalRequired(request.ItemCode, "codigo", 80)
        ?? ValidateOptionalRequired(request.Description, "descricao", 260)
        ?? ValidateOptionalPrice(request.PurchasePrice, "preco de compra")
        ?? ValidateOptionalPrice(request.SalePrice, "preco de venda")
        ?? ValidateOptionalStockBalance(request.StockBalance)
        ?? ValidateOptionalRequired(request.Cfop, "CFOP", 20)
        ?? ValidateOptionalRequired(request.Csosn, "CSOSN", 20)
        ?? ValidateOptionalRequired(request.Ncm, "NCM", 30)
        ?? ValidateOptionalRequired(request.Cst, "CST", 20)
        ?? ValidateOptionalRequired(request.Reference, "referencia", 80)
        ?? ValidateOptionalLength(request.ImageUrl1, "URL da imagem 1", 2048)
        ?? ValidateOptionalLength(request.ImageKey1, "chave da imagem 1", 512)
        ?? ValidateOptionalLength(request.ImageUrl2, "URL da imagem 2", 2048)
        ?? ValidateOptionalLength(request.ImageKey2, "chave da imagem 2", 512);

    private static string? ValidateRequired(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return $"Informe o campo {fieldName}.";
        }

        return normalized.Length > maxLength
            ? $"O campo {fieldName} deve ter no maximo {maxLength} caracteres."
            : null;
    }

    private static string? ValidateOptionalRequired(string? value, string fieldName, int maxLength) =>
        value is null ? null : ValidateRequired(value, fieldName, maxLength);

    private static string? ValidateOptionalLength(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim();
        return normalized?.Length > maxLength
            ? $"O campo {fieldName} deve ter no maximo {maxLength} caracteres."
            : null;
    }

    private static string? ValidatePrice(double value, string fieldName) =>
        IsValidPrice(value) ? null : $"O campo {fieldName} deve ser maior que zero.";

    private static string? ValidateOptionalPrice(double? value, string fieldName) =>
        value.HasValue ? ValidatePrice(value.Value, fieldName) : null;

    private static string? ValidateStockBalance(int stockBalance) =>
        stockBalance < 0 ? "O saldo de estoque deve ser maior ou igual a zero." : null;

    private static string? ValidateOptionalStockBalance(int? stockBalance) =>
        stockBalance.HasValue ? ValidateStockBalance(stockBalance.Value) : null;

    private static bool IsValidPrice(double price) =>
        !double.IsNaN(price) && !double.IsInfinity(price) && price > 0;
}
