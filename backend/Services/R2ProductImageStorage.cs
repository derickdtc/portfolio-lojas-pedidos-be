using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using backend.Configuration;
using Microsoft.Extensions.Options;

namespace backend.Services;

public sealed class R2ProductImageStorage(IOptions<R2StorageOptions> options) : IProductImageStorage
{
    private const string AuthenticationRegion = "auto";

    public async Task<ProductImageUploadResult> UploadAsync(
        int storeId,
        int productId,
        int slot,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var storageOptions = GetValidatedOptions();
        var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        if (extension == "jpeg")
        {
            extension = "jpg";
        }

        var imageKey = $"stores/{storeId}/products/{productId}/image-{slot}-{Guid.NewGuid():N}.{extension}";

        await using var fileStream = file.OpenReadStream();
        using var client = CreateClient(storageOptions);

        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = storageOptions.BucketName!,
            Key = imageKey,
            InputStream = fileStream,
            ContentType = ResolveContentType(extension),
            DisableDefaultChecksumValidation = true,
            DisablePayloadSigning = true,
            UseChunkEncoding = false,
        }, cancellationToken);

        return new ProductImageUploadResult(BuildPublicUrl(storageOptions.PublicUrl!, imageKey), imageKey);
    }

    public async Task DeleteAsync(string imageKey, CancellationToken cancellationToken)
    {
        var storageOptions = GetValidatedOptions();
        using var client = CreateClient(storageOptions);

        await client.DeleteObjectAsync(storageOptions.BucketName!, imageKey, cancellationToken);
    }

    private R2StorageOptions GetValidatedOptions()
    {
        var storageOptions = options.Value;
        storageOptions.Validate();
        return storageOptions;
    }

    private static AmazonS3Client CreateClient(R2StorageOptions storageOptions)
    {
        var credentials = new BasicAWSCredentials(
            storageOptions.AccessKeyId!,
            storageOptions.SecretAccessKey!);
        var config = new AmazonS3Config
        {
            ServiceURL = storageOptions.GetEndpoint(),
            ForcePathStyle = true,
            AuthenticationRegion = AuthenticationRegion,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        };

        return new AmazonS3Client(credentials, config);
    }

    private static string BuildPublicUrl(string publicBaseUrl, string imageKey)
    {
        var encodedKey = string.Join(
            "/",
            imageKey.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        return $"{publicBaseUrl.Trim().TrimEnd('/')}/{encodedKey}";
    }

    private static string ResolveContentType(string extension)
    {
        return extension switch
        {
            "jpg" => "image/jpeg",
            "png" => "image/png",
            "webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }
}
