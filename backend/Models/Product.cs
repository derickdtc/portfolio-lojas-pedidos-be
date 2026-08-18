using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public sealed class Product
{
    public const int MaxImages = 2;
    public const int PlannedMaxImageSizeBytes = 5 * 1024 * 1024;
    public static readonly string[] PlannedAcceptedImageFormats = ["jpg", "jpeg", "png", "webp"];

    public int Id { get; set; }

    public int StoreId { get; set; }

    public Store Store { get; set; } = null!;

    [MaxLength(80)]
    public string ItemCode { get; set; } = string.Empty;

    [MaxLength(260)]
    public string Description { get; set; } = string.Empty;

    public double PurchasePrice { get; set; }

    public double SalePrice { get; set; }

    public int StockBalance { get; set; }

    [MaxLength(20)]
    public string Cfop { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Csosn { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Ncm { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Cst { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Reference { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string? ImageUrl1 { get; set; }

    [MaxLength(512)]
    public string? ImageKey1 { get; set; }

    [MaxLength(2048)]
    public string? ImageUrl2 { get; set; }

    [MaxLength(512)]
    public string? ImageKey2 { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }
}
