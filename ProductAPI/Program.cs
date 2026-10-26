using Microsoft.EntityFrameworkCore;
using ProductAPI.Models;

namespace ProductAPI;

public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        var dbDirectory = Path.Combine(builder.Environment.ContentRootPath, "Database");
        _ = Directory.CreateDirectory(dbDirectory);
        var dbPath = Path.Combine(dbDirectory, "ProductList.db");

        // Add services to the container.

        _ = builder.Services.AddControllers();
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        _ = builder.Services.AddOpenApi();
        _ = builder.Services.AddDbContext<ProductContext>(opt =>
            opt.UseSqlite($"Data Source={dbPath}"));

        WebApplication app = builder.Build();

        using (IServiceScope scope = app.Services.CreateScope())
        {
            ProductContext dbContext = scope.ServiceProvider.GetRequiredService<ProductContext>();
            dbContext.Database.Migrate();
        }

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            _ = app.MapOpenApi();
        }

        _ = app.UseHttpsRedirection();

        _ = app.UseAuthorization();

        _ = app.MapControllers();

        _ = app.MapGet("/", () => "Hello world!");

        app.Run();
    }
}
