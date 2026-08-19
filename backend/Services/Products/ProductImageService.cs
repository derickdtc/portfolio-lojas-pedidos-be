using backend.Data;
using backend.Dtos;
using backend.Exceptions;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Products;

public sealed class ProductImageService(
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IProductImageStorage productImageStorage,
    ILogger<ProductImageService> logger)
{
    public async Task<ProductStockResponse> UploadAsync(
        int productId,
        int slot,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        ValidateSlot(slot);
        ValidateFile(file);

        var storeId = currentUserService.GetCurrentStoreId();
        var product = await FindProductAsync(productId, storeId, cancellationToken);
        var previousImageKey = GetImageKey(product, slot);
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
            throw new ApiException(
                StatusCodes.Status500InternalServerError,
                new { message = "Nao foi possivel enviar a imagem do produto." });
        }

        try
        {
            SetImage(product, slot, uploadResult.ImageUrl, uploadResult.ImageKey);
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

        return ProductMapper.ToResponse(product);
    }

    public async Task<ProductStockResponse> DeleteAsync(
        int productId,
        int slot,
        CancellationToken cancellationToken)
    {
        ValidateSlot(slot);

        var storeId = currentUserService.GetCurrentStoreId();
        var product = await FindProductAsync(productId, storeId, cancellationToken);
        var imageKey = GetImageKey(product, slot);

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
                throw new ApiException(
                    StatusCodes.Status500InternalServerError,
                    new { message = "Nao foi possivel remover a imagem do produto." });
            }
        }

        SetImage(product, slot, null, null);
        product.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ProductMapper.ToResponse(product);
    }

    private async Task<Product> FindProductAsync(int productId, int storeId, CancellationToken cancellationToken) =>
        await dbContext.Products.FirstOrDefaultAsync(
            product => product.Id == productId && product.StoreId == storeId,
            cancellationToken)
        ?? throw ApiException.NotFound("Produto nao encontrado.");

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

    private static void ValidateSlot(int slot)
    {
        if (slot is < 1 or > Product.MaxImages)
        {
            throw ApiException.BadRequest("O slot da imagem deve ser 1 ou 2.");
        }
    }

    private static void ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            throw ApiException.BadRequest("Envie uma imagem no campo file.");
        }

        if (file.Length > Product.PlannedMaxImageSizeBytes)
        {
            throw ApiException.BadRequest("A imagem deve ter no maximo 5 MB.");
        }

        var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        if (!Product.PlannedAcceptedImageFormats.Contains(extension))
        {
            throw ApiException.BadRequest("A imagem deve estar em um dos formatos: jpg, jpeg, png ou webp.");
        }
    }

    private static string? GetImageKey(Product product, int slot) =>
        slot == 1 ? product.ImageKey1 : product.ImageKey2;

    private static void SetImage(Product product, int slot, string? imageUrl, string? imageKey)
    {
        if (slot == 1)
        {
            product.ImageUrl1 = imageUrl;
            product.ImageKey1 = imageKey;
        }
        else
        {
            product.ImageUrl2 = imageUrl;
            product.ImageKey2 = imageKey;
        }
    }
}
