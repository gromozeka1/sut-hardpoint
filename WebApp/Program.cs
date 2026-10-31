using WebApp.Services;

namespace WebApp;

public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        _ = builder.Services.AddControllersWithViews();
        var productApiBaseUrl = builder.Configuration["ProductApi:BaseUrl"];
        if (!Uri.TryCreate(productApiBaseUrl, UriKind.Absolute, out Uri? productApiBaseUri))
        {
            throw new InvalidOperationException("ProductApi:BaseUrl must be configured as an absolute URI.");
        }

        _ = builder.Services.AddHttpClient<ProductApiClient>(client =>
        {
            client.BaseAddress = productApiBaseUri;
        });

        WebApplication app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            _ = app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            _ = app.UseHsts();
        }

        _ = app.UseHttpsRedirection();
        _ = app.UseRouting();

        _ = app.UseAuthorization();

        _ = app.MapStaticAssets();
        _ = app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Products}/{action=Index}/{id?}")
            .WithStaticAssets();

        app.Run();
    }
}