namespace backend.Configuration;

public sealed class R2StorageOptions
{
    public string? AccountId { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string? BucketName { get; set; }

    public string? PublicUrl { get; set; }

    public string? Endpoint { get; set; }

    public string GetEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(Endpoint))
        {
            return Endpoint.Trim().TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(AccountId))
        {
            return $"https://{AccountId.Trim()}.r2.cloudflarestorage.com";
        }

        throw new InvalidOperationException("R2 endpoint is not configured. Set R2_ENDPOINT or R2_ACCOUNT_ID.");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessKeyId))
        {
            throw new InvalidOperationException("R2_ACCESS_KEY_ID is not configured.");
        }

        if (string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            throw new InvalidOperationException("R2_SECRET_ACCESS_KEY is not configured.");
        }

        if (string.IsNullOrWhiteSpace(BucketName))
        {
            throw new InvalidOperationException("R2_BUCKET_NAME is not configured.");
        }

        if (string.IsNullOrWhiteSpace(PublicUrl))
        {
            throw new InvalidOperationException("R2_PUBLIC_URL is not configured.");
        }

        _ = GetEndpoint();
    }
}
