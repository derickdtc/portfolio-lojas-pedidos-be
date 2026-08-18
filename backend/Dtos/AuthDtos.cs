namespace backend.Dtos;

public sealed record LoginRequest(string Username, string Password, bool RememberMe);

public sealed record RegisterUserRequest(
    string? Email,
    string StoreName,
    string Username,
    string Password,
    string ConfirmPassword);

public sealed record AuthStoreResponse(string Name, string DisplayName);

public sealed record AuthUserResponse(
    int Id,
    string Username,
    string? StoreName = null,
    AuthStoreResponse? Store = null);

public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, AuthUserResponse User);
