using Microsoft.EntityFrameworkCore;

namespace ProductAPI.Models;

public class ProductContext : DbContext
{
    public ProductContext(DbContextOptions<ProductContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Product { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<Product>().HasData(
            new Product
            {
                Id = 1,
                Name = "Ryzen 7 7800X3D",
                Description = "Gaming CPU",
                ProductType = ProductType.CPU,
                Price = 399,
                Stock = 25
            },
            new Product
            {
                Id = 2,
                Name = "Dell UltraSharp 27",
                Description = "27-inch 4K monitor",
                ProductType = ProductType.MONITOR,
                Price = 499,
                Stock = 5
            },
            new Product
            {
                Id = 3,
                Name = "Logitech MX Master 3S",
                Description = "Wireless mouse",
                ProductType = ProductType.PERIPHARALS,
                Price = 99,
                Stock = 0
            });
    }
}