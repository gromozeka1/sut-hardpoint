using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProductAPI.Controllers;
using ProductAPI.Models;

namespace ProductAPI.Tests;

/// <summary>
/// Unit tests for <see cref="ProductsController"/>.
/// Each test gets its own in-memory SQLite database (real EF Core query translation,
/// not the InMemory provider) so queries such as <c>Name.Contains</c> and
/// <c>Name.ToLower()</c> behave exactly as they do against the production file database.
/// </summary>
public sealed class ProductsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProductContext _context;
    private readonly ProductsController _controller;

    public ProductsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ProductContext> options = new DbContextOptionsBuilder<ProductContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ProductContext(options);

        // Creates the schema and applies the HasData seed rows (products 1-3).
        _ = _context.Database.EnsureCreated();

        _controller = new ProductsController(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- GET list

    [Fact]
    public async Task GetProducts_ReturnsAllSeededProducts()
    {
        ActionResult<IEnumerable<Product>> result = await _controller.GetProduct(search: null, type: null);

        List<Product> products = ProductsFrom(result);
        Assert.Equal(new[] { 1, 2, 3 }, products.Select(p => p.Id));
        Assert.Equal("Ryzen 7 7800X3D", products[0].Name);
    }

    [Fact]
    public async Task GetProducts_WithSearch_ReturnsOnlyNameMatches()
    {
        ActionResult<IEnumerable<Product>> result = await _controller.GetProduct(search: "Dell", type: null);

        List<Product> products = ProductsFrom(result);
        Product single = Assert.Single(products);
        Assert.Equal(2, single.Id);
    }

    [Fact]
    public async Task GetProducts_WithSearch_IsCaseInsensitive()
    {
        ActionResult<IEnumerable<Product>> result = await _controller.GetProduct(search: "dell", type: null);

        List<Product> products = ProductsFrom(result);
        Assert.Equal(2, Assert.Single(products).Id);
    }

    [Fact]
    public async Task GetProducts_WithSearchThatMatchesNothing_ReturnsEmptyList()
    {
        ActionResult<IEnumerable<Product>> result = await _controller.GetProduct(search: "nonexistent", type: null);

        List<Product> products = ProductsFrom(result);
        Assert.Empty(products);
    }

    [Theory]
    [InlineData("MONITOR")]
    [InlineData("monitor")]
    public async Task GetProducts_WithTypeFilter_ReturnsOnlyThatType(string type)
    {
        ActionResult<IEnumerable<Product>> result = await _controller.GetProduct(search: null, type: type);

        List<Product> products = ProductsFrom(result);
        Product single = Assert.Single(products);
        Assert.Equal(ProductType.MONITOR, single.ProductType);
        Assert.Equal(2, single.Id);
    }

    [Fact]
    public async Task GetProducts_WithCombinedSearchAndType_NarrowsBothWays()
    {
        List<Product> matching = ProductsFrom(
            await _controller.GetProduct(search: "Dell", type: "MONITOR"));
        Assert.Equal(2, Assert.Single(matching).Id);

        List<Product> mismatched = ProductsFrom(
            await _controller.GetProduct(search: "Dell", type: "CPU"));
        Assert.Empty(mismatched);
    }

    [Fact]
    public async Task GetProducts_WithUnknownType_ReturnsBadRequest()
    {
        ActionResult<IEnumerable<Product>> result = await _controller.GetProduct(search: null, type: "NOPE");

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Unknown product type 'NOPE'.", badRequest.Value);
    }

    // ------------------------------------------------------------- GET by id

    [Fact]
    public async Task GetProduct_ById_WhenProductExists_ReturnsProduct()
    {
        ActionResult<Product> result = await _controller.GetProduct(1);

        Product product = ValueFrom(result);
        Assert.Equal(1, product.Id);
        Assert.Equal(ProductType.CPU, product.ProductType);
        Assert.Equal(399, product.Price);
        Assert.Equal(25, product.Stock);
    }

    [Fact]
    public async Task GetProduct_ById_WhenProductIsMissing_ReturnsNotFound()
    {
        ActionResult<Product> result = await _controller.GetProduct(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ------------------------------------------------------------------ POST

    [Fact]
    public async Task PostProduct_WithValidProduct_ReturnsCreatedWithLocationAndPersists()
    {
        Product product = new()
        {
            Name = "Test GPU",
            Description = "Unit-tested graphics card",
            ProductType = ProductType.EXTERNAL,
            Price = 120,
            Stock = 4
        };

        ActionResult<Product> result = await _controller.PostProduct(product);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("GetProduct", created.ActionName);
        Assert.Same(product, created.Value);

        Product? stored = await _context.Product.AsNoTracking().FirstOrDefaultAsync(p => p.Name == "Test GPU");
        Assert.NotNull(stored);
        Assert.Equal(4, stored.Stock);
    }

    [Fact]
    public async Task PostProduct_WhenNameAlreadyExists_ReturnsConflictWithProblemDetails()
    {
        // Same name as the seed product, different casing: the uniqueness rule is case-insensitive.
        Product product = new()
        {
            Name = "ryzen 7 7800x3d",
            ProductType = ProductType.CPU,
            Price = 1,
            Stock = 1
        };

        ActionResult<Product> result = await _controller.PostProduct(product);

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

        ProblemDetails problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Conflict", problem.Title);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Contains(product.Name, problem.Detail, StringComparison.Ordinal);

        Assert.Equal(3, await _context.Product.CountAsync());
    }

    // ------------------------------------------------------------------- PUT

    [Fact]
    public async Task PutProduct_WhenRouteIdDiffersFromBody_ReturnsBadRequest()
    {
        Product product = new() { Id = 1, Name = "Mismatch", ProductType = ProductType.CPU, Price = 1, Stock = 1 };

        IActionResult result = await _controller.PutProduct(2, product);

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task PutProduct_WhenProductIsMissing_ReturnsNotFound()
    {
        Product product = new() { Id = 999, Name = "Ghost", ProductType = ProductType.CPU, Price = 1, Stock = 1 };

        IActionResult result = await _controller.PutProduct(999, product);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PutProduct_WhenNameBelongsToAnotherProduct_ReturnsConflict()
    {
        Product? product = await _context.Product.FindAsync(2);
        Assert.NotNull(product);
        product.Name = "ryzen 7 7800x3d"; // seed product 1 already owns this name

        IActionResult result = await _controller.PutProduct(product.Id, product);

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.IsType<ProblemDetails>(conflict.Value);
    }

    [Fact]
    public async Task PutProduct_KeepingTheSameNameOnTheSameProduct_ReturnsNoContent()
    {
        Product? product = await _context.Product.FindAsync(1);
        Assert.NotNull(product);

        IActionResult result = await _controller.PutProduct(1, product);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task PutProduct_WithValidChanges_ReturnsNoContentAndPersistsThem()
    {
        Product? product = await _context.Product.FindAsync(1);
        Assert.NotNull(product);
        product.Price = 111;
        product.Stock = 7;

        IActionResult result = await _controller.PutProduct(1, product);

        Assert.IsType<NoContentResult>(result);

        Product? reloaded = await _context.Product.AsNoTracking().FirstOrDefaultAsync(p => p.Id == 1);
        Assert.NotNull(reloaded);
        Assert.Equal(111, reloaded.Price);
        Assert.Equal(7, reloaded.Stock);
    }

    // --------------------------------------------------------------- DELETE

    [Fact]
    public async Task DeleteProduct_WhenProductExists_ReturnsNoContentAndRemovesIt()
    {
        IActionResult result = await _controller.DeleteProduct(3);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await _context.Product.AnyAsync(p => p.Id == 3));
    }

    [Fact]
    public async Task DeleteProduct_WhenProductIsMissing_ReturnsNotFound()
    {
        IActionResult result = await _controller.DeleteProduct(999);

        Assert.IsType<NotFoundResult>(result);
    }

    // -------------------------------------------------------------- contract

    [Fact]
    public void ProductType_WireValues_AreStable()
    {
        // The API serialises enums as numbers; reordering or inserting values changes the contract.
        Assert.Equal(0, (int)ProductType.CPU);
        Assert.Equal(1, (int)ProductType.MONITOR);
        Assert.Equal(2, (int)ProductType.PERIPHARALS);
        Assert.Equal(3, (int)ProductType.EXTERNAL);
    }

    /// <summary>
    /// Success paths return the payload through the implicit <c>ActionResult&lt;T&gt;</c>
    /// conversion, which ASP.NET Core serialises as 200 OK — the value therefore arrives in
    /// <c>Value</c>, while error paths put an <c>IActionResult</c> into <c>Result</c>.
    /// </summary>
    private static List<Product> ProductsFrom(ActionResult<IEnumerable<Product>> result)
    {
        Assert.Null(result.Result);
        return Assert.IsType<List<Product>>(result.Value);
    }

    private static T ValueFrom<T>(ActionResult<T> result)
    {
        Assert.Null(result.Result);
        return Assert.IsType<T>(result.Value);
    }
}
