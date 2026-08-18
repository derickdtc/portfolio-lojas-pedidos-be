using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public sealed class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order? Order { get; set; }

    public int? ProductId { get; set; }

    public Product? Product { get; set; }

    [MaxLength(80)]
    public string ProductItemCode { get; set; } = string.Empty;

    [MaxLength(260)]
    public string ProductDescription { get; set; } = string.Empty;

    [MaxLength(80)]
    public string ProductReference { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Cfop { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Csosn { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Ncm { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Cst { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public double SalePrice { get; set; }

    public double LineTotal { get; set; }
}
