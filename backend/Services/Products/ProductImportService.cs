using backend.Data;
using backend.Dtos;
using backend.Exceptions;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Products;

public sealed class ProductImportService(
    AppDbContext dbContext,
    ProductSpreadsheetImporter spreadsheetImporter,
    ICurrentUserService currentUserService)
{
    public async Task<ProductImportResponse> ImportAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw ApiException.BadRequest("Envie uma planilha .xlsx com o estoque.");
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.BadRequest("O arquivo precisa estar no formato .xlsx.");
        }

        ProductSpreadsheetImportResult parsed;
        await using (var stream = file.OpenReadStream())
        {
            parsed = await spreadsheetImporter.ReadAsync(stream, cancellationToken);
        }

        if (parsed.Products.Count == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, new
            {
                message = "Nenhum produto valido foi encontrado na planilha.",
                warnings = parsed.Warnings,
            });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var replaced = await dbContext.Products.CountAsync(
            product => product.StoreId == storeId,
            cancellationToken);

        await dbContext.Products
            .Where(product => product.StoreId == storeId)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var batch in parsed.Products.Chunk(500))
        {
            var createdAtUtc = DateTime.UtcNow;
            dbContext.Products.AddRange(batch.Select(product => new Product
            {
                StoreId = storeId,
                ItemCode = product.ItemCode,
                Description = product.Description,
                PurchasePrice = product.PurchasePrice,
                SalePrice = product.SalePrice,
                StockBalance = product.StockBalance,
                Cfop = product.Cfop,
                Csosn = product.Csosn,
                Ncm = product.Ncm,
                Cst = product.Cst,
                Reference = product.Reference,
                CreatedAtUtc = createdAtUtc,
            }));

            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(cancellationToken);
        return new ProductImportResponse(
            parsed.Products.Count,
            replaced,
            parsed.Skipped,
            parsed.Warnings.Take(20).ToList());
    }
}
