using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public sealed class User
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(80)]
    public string UsernameNormalized { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? Email { get; set; }

    [MaxLength(100)]
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAtUtc { get; set; }

    public List<StoreUser> StoreUsers { get; set; } = [];
}
