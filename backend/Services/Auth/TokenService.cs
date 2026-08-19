using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using backend.Models;
using Microsoft.IdentityModel.Tokens;

namespace backend.Services.Auth;

public sealed class TokenService(IConfiguration configuration)
{
    public string CreateAccessToken(User user, StoreUser storeUser, DateTime expiresAtUtc)
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
}
