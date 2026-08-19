using backend.Dtos;
using backend.Models;

namespace backend.Services.Products;

internal static class ProductMapper
{
    public static ProductStockResponse ToResponse(Product product) =>
        new(
            product.Id,
            product.ItemCode,
            product.Description,
            product.PurchasePrice,
            product.SalePrice,
            product.StockBalance,
            product.Cfop,
            product.Csosn,
            product.Ncm,
            product.Cst,
            product.Reference,
            product.ImageUrl1,
            product.ImageKey1,
            product.ImageUrl2,
            product.ImageKey2);
}
