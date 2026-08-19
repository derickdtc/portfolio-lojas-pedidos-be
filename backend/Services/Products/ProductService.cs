using backend.Data;
using backend.Dtos;
using backend.Exceptions;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Products;

public sealed class ProductService(AppDbContext dbContext, ICurrentUserService currentUserService)
{
    public async Task<IReadOnlyList<ProductStockResponse>> GetAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        return await BuildQuery(storeId, search)
            .OrderBy(product => product.Description)
            .ThenBy(product => product.Id)
            .Select(product => new ProductStockResponse(
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
                product.ImageKey2))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResponse<ProductStockResponse>> GetPagedAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);

        var storeId = currentUserService.GetCurrentStoreId();
        var query = BuildQuery(storeId, search);
        var totalCount = await query.CountAsync(cancellationToken);
        var products = await query
            .OrderBy(product => product.Description)
            .ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductStockResponse(
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
                product.ImageKey2))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ProductStockResponse>(
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            products);
    }

    public async Task<ProductStockResponse> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                existingProduct => existingProduct.Id == id && existingProduct.StoreId == storeId,
                cancellationToken)
            ?? throw ApiException.NotFound();

        return ProductMapper.ToResponse(product);
    }

    public async Task<ProductStockResponse> CreateAsync(
        CreateProductRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("Informe os dados do produto.");
        }

        var validationError = ProductRequestValidator.Validate(request);
        if (validationError is not null)
        {
            throw ApiException.BadRequest(validationError);
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var itemCode = NormalizeRequired(request.ItemCode);
        var itemCodeExists = await dbContext.Products.AnyAsync(
            product => product.StoreId == storeId && product.ItemCode == itemCode,
            cancellationToken);

        if (itemCodeExists)
        {
            throw ApiException.BadRequest("Ja existe um produto com este codigo nesta loja.");
        }

        var product = new Product
        {
            StoreId = storeId,
            ItemCode = itemCode,
            Description = NormalizeRequired(request.Description),
            PurchasePrice = request.PurchasePrice,
            SalePrice = request.SalePrice,
            StockBalance = request.StockBalance,
            Cfop = NormalizeRequired(request.Cfop),
            Csosn = NormalizeRequired(request.Csosn),
            Ncm = NormalizeRequired(request.Ncm),
            Cst = NormalizeRequired(request.Cst),
            Reference = NormalizeRequired(request.Reference),
            ImageUrl1 = NormalizeOptional(request.ImageUrl1),
            ImageKey1 = NormalizeOptional(request.ImageKey1),
            ImageUrl2 = NormalizeOptional(request.ImageUrl2),
            ImageKey2 = NormalizeOptional(request.ImageKey2),
            CreatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ProductMapper.ToResponse(product);
    }

    public async Task<ProductStockResponse> UpdateAsync(
        int id,
        UpdateProductRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("Informe os dados do produto.");
        }

        var validationError = ProductRequestValidator.Validate(request);
        if (validationError is not null)
        {
            throw ApiException.BadRequest(validationError);
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var product = await dbContext.Products.FirstOrDefaultAsync(
            existingProduct => existingProduct.Id == id && existingProduct.StoreId == storeId,
            cancellationToken)
            ?? throw ApiException.NotFound("Produto nao encontrado.");

        if (request.ItemCode is not null)
        {
            var itemCode = NormalizeRequired(request.ItemCode);
            var itemCodeExists = await dbContext.Products.AnyAsync(
                existingProduct => existingProduct.StoreId == storeId
                    && existingProduct.Id != id
                    && existingProduct.ItemCode == itemCode,
                cancellationToken);

            if (itemCodeExists)
            {
                throw ApiException.BadRequest("Ja existe um produto com este codigo nesta loja.");
            }

            product.ItemCode = itemCode;
        }

        ApplyUpdate(product, request);
        product.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ProductMapper.ToResponse(product);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        var product = await dbContext.Products.FirstOrDefaultAsync(
            existingProduct => existingProduct.Id == id && existingProduct.StoreId == storeId,
            cancellationToken)
            ?? throw ApiException.NotFound("Produto nao encontrado.");

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Product> BuildQuery(int storeId, string? search)
    {
        var query = dbContext.Products.AsNoTracking().Where(product => product.StoreId == storeId);
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var pattern = $"%{search.Trim()}%";
        return query.Where(product =>
            EF.Functions.ILike(product.Description, pattern)
            || EF.Functions.ILike(product.ItemCode, pattern)
            || EF.Functions.ILike(product.Reference, pattern)
            || EF.Functions.ILike(product.Cfop, pattern)
            || EF.Functions.ILike(product.Csosn, pattern)
            || EF.Functions.ILike(product.Ncm, pattern)
            || EF.Functions.ILike(product.Cst, pattern));
    }

    private static void ApplyUpdate(Product product, UpdateProductRequest request)
    {
        if (request.Description is not null) product.Description = NormalizeRequired(request.Description);
        if (request.PurchasePrice.HasValue) product.PurchasePrice = request.PurchasePrice.Value;
        if (request.SalePrice.HasValue) product.SalePrice = request.SalePrice.Value;
        if (request.StockBalance.HasValue) product.StockBalance = request.StockBalance.Value;
        if (request.Cfop is not null) product.Cfop = NormalizeRequired(request.Cfop);
        if (request.Csosn is not null) product.Csosn = NormalizeRequired(request.Csosn);
        if (request.Ncm is not null) product.Ncm = NormalizeRequired(request.Ncm);
        if (request.Cst is not null) product.Cst = NormalizeRequired(request.Cst);
        if (request.Reference is not null) product.Reference = NormalizeRequired(request.Reference);
        if (request.ImageUrl1 is not null) product.ImageUrl1 = NormalizeOptional(request.ImageUrl1);
        if (request.ImageKey1 is not null) product.ImageKey1 = NormalizeOptional(request.ImageKey1);
        if (request.ImageUrl2 is not null) product.ImageUrl2 = NormalizeOptional(request.ImageUrl2);
        if (request.ImageKey2 is not null) product.ImageKey2 = NormalizeOptional(request.ImageKey2);
    }

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1)
        {
            throw ApiException.BadRequest("O parametro page deve ser maior ou igual a 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw ApiException.BadRequest("O parametro pageSize deve estar entre 1 e 100.");
        }

        if (page > (int.MaxValue / pageSize) + 1)
        {
            throw ApiException.BadRequest("A combinacao de page e pageSize e muito grande.");
        }
    }

    private static string NormalizeRequired(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
