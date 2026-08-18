namespace backend.Services;

public interface IProductImageStorage
{
    Task<ProductImageUploadResult> UploadAsync(
        int storeId,
        int productId,
        int slot,
        IFormFile file,
        CancellationToken cancellationToken);

    Task DeleteAsync(string imageKey, CancellationToken cancellationToken);
}

public sealed record ProductImageUploadResult(string ImageUrl, string ImageKey);
