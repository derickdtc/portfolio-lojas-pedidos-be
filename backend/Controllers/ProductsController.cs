using backend.Data;
using backend.Dtos;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ProductsController(
    AppDbContext dbContext,
    ProductSpreadsheetImporter spreadsheetImporter,
    ICurrentUserService currentUserService,
    IProductImageStorage productImageStorage,
    ILogger<ProductsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductStockResponse>>> Get(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        var products = await BuildProductsQuery(storeId, search)
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

        return Ok(products);
    }

    // This endpoint preserves the existing GET /api/products response while allowing
    // clients with large inventories to limit database, application, and network usage.
    [HttpGet("paged")]
    public async Task<ActionResult<PagedResponse<ProductStockResponse>>> GetPaged(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidatePagination(page, pageSize, out var errorMessage))
        {
            return BadRequest(new { message = errorMessage });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var query = BuildProductsQuery(storeId, search);
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

        return Ok(new PagedResponse<ProductStockResponse>(
            page,
            pageSize,
            totalCount,
            GetTotalPages(totalCount, pageSize),
            products));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductStockResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                existingProduct => existingProduct.Id == id && existingProduct.StoreId == storeId,
                cancellationToken);

        return product is null ? NotFound() : Ok(ToResponse(product));
    }

    [HttpPost]
    public async Task<ActionResult<ProductStockResponse>> Create(
        [FromBody] CreateProductRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Informe os dados do produto." });
        }

        var validationError = ValidateCreateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var itemCode = NormalizeRequired(request.ItemCode);
        var itemCodeExists = await dbContext.Products
            .AnyAsync(
                product => product.StoreId == storeId && product.ItemCode == itemCode,
                cancellationToken);

        if (itemCodeExists)
        {
            return BadRequest(new { message = "Ja existe um produto com este codigo nesta loja." });
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

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ToResponse(product));
    }

    [HttpPut("{id:int}")]
    [HttpPatch("{id:int}")]
    [HttpPut("{id:int}/sale-price")]
    [HttpPatch("{id:int}/sale-price")]
    public async Task<ActionResult<ProductStockResponse>> Update(
        int id,
        [FromBody] UpdateProductRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Informe os dados do produto." });
        }

        var validationError = ValidateUpdateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var product = await dbContext.Products
            .FirstOrDefaultAsync(
                existingProduct => existingProduct.Id == id && existingProduct.StoreId == storeId,
                cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = "Produto nao encontrado." });
        }

        if (request.ItemCode is not null)
        {
            var itemCode = NormalizeRequired(request.ItemCode);
            var itemCodeExists = await dbContext.Products
                .AnyAsync(
                    existingProduct => existingProduct.StoreId == storeId
                        && existingProduct.Id != id
                        && existingProduct.ItemCode == itemCode,
                    cancellationToken);

            if (itemCodeExists)
            {
                return BadRequest(new { message = "Ja existe um produto com este codigo nesta loja." });
            }

            product.ItemCode = itemCode;
        }

        if (request.Description is not null)
        {
            product.Description = NormalizeRequired(request.Description);
        }

        if (request.PurchasePrice.HasValue)
        {
            product.PurchasePrice = request.PurchasePrice.Value;
        }

        if (request.SalePrice.HasValue)
        {
            product.SalePrice = request.SalePrice.Value;
        }

        if (request.StockBalance.HasValue)
        {
            product.StockBalance = request.StockBalance.Value;
        }

        if (request.Cfop is not null)
        {
            product.Cfop = NormalizeRequired(request.Cfop);
        }

        if (request.Csosn is not null)
        {
            product.Csosn = NormalizeRequired(request.Csosn);
        }

        if (request.Ncm is not null)
        {
            product.Ncm = NormalizeRequired(request.Ncm);
        }

        if (request.Cst is not null)
        {
            product.Cst = NormalizeRequired(request.Cst);
        }

        if (request.Reference is not null)
        {
            product.Reference = NormalizeRequired(request.Reference);
        }

        if (request.ImageUrl1 is not null)
        {
            product.ImageUrl1 = NormalizeOptional(request.ImageUrl1);
        }

        if (request.ImageKey1 is not null)
        {
            product.ImageKey1 = NormalizeOptional(request.ImageKey1);
        }

        if (request.ImageUrl2 is not null)
        {
            product.ImageUrl2 = NormalizeOptional(request.ImageUrl2);
        }

        if (request.ImageKey2 is not null)
        {
            product.ImageKey2 = NormalizeOptional(request.ImageKey2);
        }

        product.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(product));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        var product = await dbContext.Products
            .FirstOrDefaultAsync(
                existingProduct => existingProduct.Id == id && existingProduct.StoreId == storeId,
                cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = "Produto nao encontrado." });
        }

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{productId:int}/images/{slot:int}")]
    [RequestSizeLimit(Product.PlannedMaxImageSizeBytes + 1_000_000)]
    public async Task<ActionResult<ProductStockResponse>> UploadImage(
        int productId,
        int slot,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!IsValidImageSlot(slot))
        {
            return BadRequest(new { message = "O slot da imagem deve ser 1 ou 2." });
        }

        var validationError = ValidateProductImageFile(file);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var product = await dbContext.Products
            .FirstOrDefaultAsync(
                existingProduct => existingProduct.Id == productId && existingProduct.StoreId == storeId,
                cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = "Produto nao encontrado." });
        }

        var previousImageKey = GetProductImageKey(product, slot);
        ProductImageUploadResult uploadResult;

        try
        {
            uploadResult = await productImageStorage.UploadAsync(
                storeId,
                product.Id,
                slot,
                file!,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to upload product image. StoreId: {StoreId}. ProductId: {ProductId}. Slot: {Slot}.",
                storeId,
                product.Id,
                slot);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "Nao foi possivel enviar a imagem do produto." });
        }

        try
        {
            SetProductImage(product, slot, uploadResult.ImageUrl, uploadResult.ImageKey);
            product.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await TryDeleteUploadedImageAsync(uploadResult.ImageKey, cancellationToken);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(previousImageKey)
            && !string.Equals(previousImageKey, uploadResult.ImageKey, StringComparison.Ordinal))
        {
            await TryDeleteReplacedImageAsync(previousImageKey, product.Id, slot, cancellationToken);
        }

        return Ok(ToResponse(product));
    }

    [HttpDelete("{productId:int}/images/{slot:int}")]
    public async Task<ActionResult<ProductStockResponse>> DeleteImage(
        int productId,
        int slot,
        CancellationToken cancellationToken)
    {
        if (!IsValidImageSlot(slot))
        {
            return BadRequest(new { message = "O slot da imagem deve ser 1 ou 2." });
        }

        var storeId = currentUserService.GetCurrentStoreId();
        var product = await dbContext.Products
            .FirstOrDefaultAsync(
                existingProduct => existingProduct.Id == productId && existingProduct.StoreId == storeId,
                cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = "Produto nao encontrado." });
        }

        var imageKey = GetProductImageKey(product, slot);

        if (!string.IsNullOrWhiteSpace(imageKey))
        {
            try
            {
                await productImageStorage.DeleteAsync(imageKey, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to delete product image. StoreId: {StoreId}. ProductId: {ProductId}. Slot: {Slot}.",
                    storeId,
                    product.Id,
                    slot);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { message = "Nao foi possivel remover a imagem do produto." });
            }
        }

        SetProductImage(product, slot, null, null);
        product.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(product));
    }

    [HttpPost("import")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ProductImportResponse>> Import(
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Envie uma planilha .xlsx com o estoque." });
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "O arquivo precisa estar no formato .xlsx." });
        }

        ProductSpreadsheetImportResult parsed;

        await using (var stream = file.OpenReadStream())
        {
            parsed = await spreadsheetImporter.ReadAsync(stream, cancellationToken);
        }

        if (parsed.Products.Count == 0)
        {
            return BadRequest(new
            {
                message = "Nenhum produto valido foi encontrado na planilha.",
                warnings = parsed.Warnings,
            });
        }

        var storeId = currentUserService.GetCurrentStoreId();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var replaced = await dbContext.Products
            .CountAsync(product => product.StoreId == storeId, cancellationToken);
        await dbContext.Products
            .Where(product => product.StoreId == storeId)
            .ExecuteDeleteAsync(cancellationToken);

        // Save in bounded batches so a large spreadsheet does not leave every new
        // entity tracked by EF until the import finishes. The surrounding transaction
        // keeps the replacement atomic.
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

        return Ok(new ProductImportResponse(
            parsed.Products.Count,
            replaced,
            parsed.Skipped,
            parsed.Warnings.Take(20).ToList()));
    }

    private static ProductStockResponse ToResponse(Product product)
    {
        return new ProductStockResponse(
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

    private IQueryable<Product> BuildProductsQuery(int storeId, string? search)
    {
        var query = dbContext.Products
            .AsNoTracking()
            .Where(product => product.StoreId == storeId);

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

    private static bool TryValidatePagination(int page, int pageSize, out string? errorMessage)
    {
        if (page < 1)
        {
            errorMessage = "O parametro page deve ser maior ou igual a 1.";
            return false;
        }

        if (pageSize is < 1 or > 100)
        {
            errorMessage = "O parametro pageSize deve estar entre 1 e 100.";
            return false;
        }

        if (page > (int.MaxValue / pageSize) + 1)
        {
            errorMessage = "A combinacao de page e pageSize e muito grande.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private static int GetTotalPages(int totalCount, int pageSize)
    {
        return totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    private async Task TryDeleteUploadedImageAsync(string imageKey, CancellationToken cancellationToken)
    {
        try
        {
            await productImageStorage.DeleteAsync(imageKey, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to cleanup newly uploaded product image after database save failure. ImageKey: {ImageKey}.",
                imageKey);
        }
    }

    private async Task TryDeleteReplacedImageAsync(
        string imageKey,
        int productId,
        int slot,
        CancellationToken cancellationToken)
    {
        try
        {
            await productImageStorage.DeleteAsync(imageKey, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to delete replaced product image. ProductId: {ProductId}. Slot: {Slot}. ImageKey: {ImageKey}.",
                productId,
                slot,
                imageKey);
        }
    }

    private static bool IsValidImageSlot(int slot)
    {
        return slot is >= 1 and <= Product.MaxImages;
    }

    private static string? ValidateProductImageFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return "Envie uma imagem no campo file.";
        }

        if (file.Length > Product.PlannedMaxImageSizeBytes)
        {
            return "A imagem deve ter no maximo 5 MB.";
        }

        var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        if (!Product.PlannedAcceptedImageFormats.Contains(extension))
        {
            return "A imagem deve estar em um dos formatos: jpg, jpeg, png ou webp.";
        }

        return null;
    }

    private static string? GetProductImageKey(Product product, int slot)
    {
        return slot switch
        {
            1 => product.ImageKey1,
            2 => product.ImageKey2,
            _ => null,
        };
    }

    private static void SetProductImage(Product product, int slot, string? imageUrl, string? imageKey)
    {
        if (slot == 1)
        {
            product.ImageUrl1 = imageUrl;
            product.ImageKey1 = imageKey;
            return;
        }

        product.ImageUrl2 = imageUrl;
        product.ImageKey2 = imageKey;
    }

    private static string? ValidateCreateRequest(CreateProductRequest request)
    {
        return ValidateRequired(request.ItemCode, "codigo", 80)
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
    }

    private static string? ValidateUpdateRequest(UpdateProductRequest request)
    {
        return ValidateOptionalRequired(request.ItemCode, "codigo", 80)
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
    }

    private static string NormalizeRequired(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

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

    private static string? ValidateOptionalRequired(string? value, string fieldName, int maxLength)
    {
        return value is null ? null : ValidateRequired(value, fieldName, maxLength);
    }

    private static string? ValidateOptionalLength(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim();
        return normalized?.Length > maxLength
            ? $"O campo {fieldName} deve ter no maximo {maxLength} caracteres."
            : null;
    }

    private static string? ValidatePrice(double value, string fieldName)
    {
        return IsValidPrice(value) ? null : $"O campo {fieldName} deve ser maior que zero.";
    }

    private static string? ValidateOptionalPrice(double? value, string fieldName)
    {
        return value.HasValue ? ValidatePrice(value.Value, fieldName) : null;
    }

    private static string? ValidateStockBalance(int stockBalance)
    {
        return stockBalance < 0 ? "O saldo de estoque deve ser maior ou igual a zero." : null;
    }

    private static string? ValidateOptionalStockBalance(int? stockBalance)
    {
        return stockBalance.HasValue ? ValidateStockBalance(stockBalance.Value) : null;
    }

    private static bool IsValidPrice(double price)
    {
        return !double.IsNaN(price)
            && !double.IsInfinity(price)
            && price > 0;
    }
}
