using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public sealed class StoreUser
{
    public int Id { get; set; }

    public int StoreId { get; set; }

    public Store Store { get; set; } = null!;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    [MaxLength(30)]
    public string Role { get; set; } = StoreRoles.Owner;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
