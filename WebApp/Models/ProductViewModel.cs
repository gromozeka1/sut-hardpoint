using System.ComponentModel.DataAnnotations;

namespace WebApp.Models;

public class ProductViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [EnumDataType(typeof(ProductType))]
    [Display(Name = "Product type")]
    public ProductType ProductType { get; set; }

    [Range(0, int.MaxValue)]
    public int Price { get; set; }

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }
}

public enum ProductType
{
    [Display(Name = "CPU")]
    CPU,
    [Display(Name = "Monitor")]
    MONITOR,
    [Display(Name = "Peripherals")]
    PERIPHERALS,
    [Display(Name = "External")]
    EXTERNAL
}
