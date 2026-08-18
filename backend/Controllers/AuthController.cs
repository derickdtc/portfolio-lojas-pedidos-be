using System.IdentityModel.Tokens.Jwt;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using backend.Data;
using backend.Dtos;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(AppDbContext dbContext, IConfiguration configuration) : ControllerBase
{
    private static readonly TimeSpan DefaultSessionDuration = TimeSpan.FromHours(8);
    private static readonly TimeSpan PersistentSessionDuration = TimeSpan.FromDays(30);
    private static readonly EmailAddressAttribute EmailValidator = new();

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthUserResponse>> Register(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var username = NormalizeRequired(request.Username);
        if (username is null)
        {
            return BadRequest(new { message = "Informe o usuario." });
        }

        var storeName = NormalizeRequired(request.StoreName);
        if (storeName is null)
        {
            return BadRequest(new { message = "Informe a loja." });
        }

        var password = request.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            return BadRequest(new { message = "Informe a senha." });
        }

        if (!string.Equals(password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "A confirmacao de senha nao confere." });
        }

        var email = NormalizeOptional(request.Email);
        if (email is not null && !EmailValidator.IsValid(email))
        {
            return BadRequest(new { message = "Informe um e-mail valido." });
        }

        var normalizedUsername = AppDbSeeder.NormalizeUsername(username);
        var usernameAlreadyExists = await dbContext.Users
            .AnyAsync(existingUser => existingUser.UsernameNormalized == normalizedUsername, cancellationToken);

        if (usernameAlreadyExists)
        {
            return Conflict(new { message = "Usuario ja cadastrado." });
        }

        var store = await dbContext.Stores
            .FirstOrDefaultAsync(
                existingStore => existingStore.Name == storeName && existingStore.IsActive,
                cancellationToken);

        if (store is null)
        {
            return BadRequest(new { message = "Loja nao encontrada. Informe o nome exatamente como cadastrado." });
        }

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

        return StatusCode(
            StatusCodes.Status201Created,
            ToAuthUserResponse(user.Id, user.Username, store));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Informe usuario e senha." });
        }

        var normalizedUsername = AppDbSeeder.NormalizeUsername(request.Username);
        var user = await dbContext.Users
            .FirstOrDefaultAsync(
                existingUser => existingUser.UsernameNormalized == normalizedUsername && existingUser.IsActive,
                cancellationToken);

        if (user is null || !IsPasswordValid(user.PasswordHash, request.Password))
        {
            return Unauthorized(new { message = "Usuario ou senha invalidos." });
        }

        var storeUser = await dbContext.StoreUsers
            .AsNoTracking()
            .Include(existingStoreUser => existingStoreUser.Store)
            .Where(existingStoreUser => existingStoreUser.UserId == user.Id)
            .Where(existingStoreUser => existingStoreUser.IsActive)
            .Where(existingStoreUser => existingStoreUser.Store.IsActive)
            .OrderBy(existingStoreUser => existingStoreUser.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (storeUser is null)
        {
            return Unauthorized(new { message = "Usuario sem loja vinculada." });
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var expiresAtUtc = DateTime.UtcNow.Add(
            request.RememberMe ? PersistentSessionDuration : DefaultSessionDuration);

        return Ok(new AuthResponse(
            CreateToken(user, storeUser, expiresAtUtc),
            expiresAtUtc,
            ToAuthUserResponse(user.Id, user.Username, storeUser.Store)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserResponse>> Me(CancellationToken cancellationToken)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var username = User.FindFirstValue(ClaimTypes.Name);
        var storeId = User.FindFirstValue("storeId");

        if (!int.TryParse(id, out var userId)
            || !int.TryParse(storeId, out var currentStoreId)
            || string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var storeUser = await dbContext.StoreUsers
            .AsNoTracking()
            .Include(existingStoreUser => existingStoreUser.Store)
            .Where(existingStoreUser => existingStoreUser.UserId == userId)
            .Where(existingStoreUser => existingStoreUser.StoreId == currentStoreId)
            .Where(existingStoreUser => existingStoreUser.User.IsActive)
            .Where(existingStoreUser => existingStoreUser.IsActive)
            .Where(existingStoreUser => existingStoreUser.Store.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (storeUser is null)
        {
            return Unauthorized();
        }

        return Ok(ToAuthUserResponse(userId, username, storeUser.Store));
    }

    private string CreateToken(User user, StoreUser storeUser, DateTime expiresAtUtc)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("userId", user.Id.ToString()),
                new Claim("storeId", storeUser.StoreId.ToString()),
                new Claim("storeName", storeUser.Store.Name),
                new Claim("role", storeUser.Role),
                new Claim(ClaimTypes.Role, storeUser.Role),
            ],
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static AuthUserResponse ToAuthUserResponse(int userId, string username, Store store)
    {
        return new AuthUserResponse(
            userId,
            username,
            store.Name,
            new AuthStoreResponse(store.Name, store.Name));
    }

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
