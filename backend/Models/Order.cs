using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public sealed class Order
{
    public int Id { get; set; }

    public int StoreId { get; set; }

    public Store Store { get; set; } = null!;

    public int CreatedByUserId { get; set; }

    [MaxLength(80)]
    public string CreatedByUsername { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? CustomerName { get; set; }

    [MaxLength(1000)]
    public string? Observations { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "created";

    public double TotalAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = [];
}
