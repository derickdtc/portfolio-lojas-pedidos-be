namespace backend.Dtos;

public sealed record StoreResponse(
    int Id,
    string Name,
    string? Cnpj,
    string? Phone,
    string? Address,
    bool IsActive,
    string Role,
    DateTime CreatedAtUtc);

public sealed record CreateStoreRequest(
    string Name,
    string? Cnpj,
    string? Phone,
    string? Address);

public sealed record RegisterStoreRequest(string Name);

public sealed record UpdateStoreRequest(
    string? Name,
    string? Cnpj,
    string? Phone,
    string? Address,
    bool? IsActive);
