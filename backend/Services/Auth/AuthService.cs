using System.ComponentModel.DataAnnotations;
using backend.Data;
using backend.Dtos;
using backend.Exceptions;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Auth;

public sealed class AuthService(
    AppDbContext dbContext,
    TokenService tokenService,
    ICurrentUserService currentUserService)
{
    private static readonly TimeSpan DefaultSessionDuration = TimeSpan.FromHours(8);
    private static readonly TimeSpan PersistentSessionDuration = TimeSpan.FromDays(30);
    private static readonly EmailAddressAttribute EmailValidator = new();

    public async Task<AuthUserResponse> RegisterAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var username = NormalizeRequired(request.Username)
            ?? throw ApiException.BadRequest("Informe o usuario.");
        var storeName = NormalizeRequired(request.StoreName)
            ?? throw ApiException.BadRequest("Informe a loja.");
        var password = request.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            throw ApiException.BadRequest("Informe a senha.");
        }

        if (!string.Equals(password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            throw ApiException.BadRequest("A confirmacao de senha nao confere.");
        }

        var email = NormalizeOptional(request.Email);
        if (email is not null && !EmailValidator.IsValid(email))
        {
            throw ApiException.BadRequest("Informe um e-mail valido.");
        }

        var normalizedUsername = AppDbSeeder.NormalizeUsername(username);
        var usernameAlreadyExists = await dbContext.Users
            .AnyAsync(user => user.UsernameNormalized == normalizedUsername, cancellationToken);

        if (usernameAlreadyExists)
        {
            throw ApiException.Conflict("Usuario ja cadastrado.");
        }

        var store = await dbContext.Stores
            .FirstOrDefaultAsync(
                existingStore => existingStore.Name == storeName && existingStore.IsActive,
                cancellationToken)
            ?? throw ApiException.BadRequest(
                "Loja nao encontrada. Informe o nome exatamente como cadastrado.");

        var user = new User
        {
            Username = username,
            UsernameNormalized = normalizedUsername,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Users.Add(user);
        dbContext.StoreUsers.Add(new StoreUser
        {
            Store = store,
            User = user,
            Role = StoreRoles.Owner,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(user.Id, user.Username, store);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw ApiException.BadRequest("Informe usuario e senha.");
        }

        var normalizedUsername = AppDbSeeder.NormalizeUsername(request.Username);
        var user = await dbContext.Users.FirstOrDefaultAsync(
            existingUser => existingUser.UsernameNormalized == normalizedUsername && existingUser.IsActive,
            cancellationToken);

        if (user is null || !IsPasswordValid(user.PasswordHash, request.Password))
        {
            throw ApiException.Unauthorized("Usuario ou senha invalidos.");
        }

        var storeUser = await dbContext.StoreUsers
            .AsNoTracking()
            .Include(existingStoreUser => existingStoreUser.Store)
            .Where(existingStoreUser => existingStoreUser.UserId == user.Id)
            .Where(existingStoreUser => existingStoreUser.IsActive)
            .Where(existingStoreUser => existingStoreUser.Store.IsActive)
            .OrderBy(existingStoreUser => existingStoreUser.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw ApiException.Unauthorized("Usuario sem loja vinculada.");

        user.LastLoginAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var expiresAtUtc = DateTime.UtcNow.Add(
            request.RememberMe ? PersistentSessionDuration : DefaultSessionDuration);

        return new AuthResponse(
            tokenService.CreateAccessToken(user, storeUser, expiresAtUtc),
            expiresAtUtc,
            ToResponse(user.Id, user.Username, storeUser.Store));
    }

    public async Task<AuthUserResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        int userId;
        int storeId;
        string username;

        try
        {
            userId = currentUserService.GetUserId();
            storeId = currentUserService.GetCurrentStoreId();
            username = currentUserService.GetUsername();
        }
        catch (UnauthorizedAccessException)
        {
            throw ApiException.Unauthorized();
        }

        var storeUser = await dbContext.StoreUsers
            .AsNoTracking()
            .Include(existingStoreUser => existingStoreUser.Store)
            .Where(existingStoreUser => existingStoreUser.UserId == userId)
            .Where(existingStoreUser => existingStoreUser.StoreId == storeId)
            .Where(existingStoreUser => existingStoreUser.User.IsActive)
            .Where(existingStoreUser => existingStoreUser.IsActive)
            .Where(existingStoreUser => existingStoreUser.Store.IsActive)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw ApiException.Unauthorized();

        return ToResponse(userId, username, storeUser.Store);
    }

    private static AuthUserResponse ToResponse(int userId, string username, Store store) =>
        new(userId, username, store.Name, new AuthStoreResponse(store.Name, store.Name));

    private static string? NormalizeRequired(string? value)
    {
        var normalized = NormalizeOptional(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsPasswordValid(string passwordHash, string password)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
