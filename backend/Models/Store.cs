using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public sealed class Store
{
    public int Id { get; set; }

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Cnpj { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(260)]
    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<StoreUser> StoreUsers { get; set; } = [];

    public List<Product> Products { get; set; } = [];

    public List<Order> Orders { get; set; } = [];
}
