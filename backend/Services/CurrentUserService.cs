using System.Security.Claims;

namespace backend.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public int GetUserId()
    {
        var userId = FindClaimValue(ClaimTypes.NameIdentifier) ?? FindClaimValue("userId");

        if (!int.TryParse(userId, out var parsedUserId))
        {
            throw new UnauthorizedAccessException("Usuario nao autenticado.");
        }

        return parsedUserId;
    }

    public string GetUsername()
    {
        var username = FindClaimValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new UnauthorizedAccessException("Usuario nao autenticado.");
        }

        return username;
    }

    public int GetCurrentStoreId()
    {
        var storeId = FindClaimValue("storeId");

        if (!int.TryParse(storeId, out var parsedStoreId))
        {
            throw new UnauthorizedAccessException("Loja nao encontrada no token.");
        }

        return parsedStoreId;
    }

    public string GetRole()
    {
        var role = FindClaimValue(ClaimTypes.Role) ?? FindClaimValue("role");

        if (string.IsNullOrWhiteSpace(role))
        {
            throw new UnauthorizedAccessException("Permissao nao encontrada.");
        }

        return role;
    }

    private string? FindClaimValue(string claimType)
    {
        return httpContextAccessor.HttpContext?.User.FindFirstValue(claimType);
    }
}
