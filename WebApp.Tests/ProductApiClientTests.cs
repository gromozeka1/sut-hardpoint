using System.Net;
using System.Web;
using WebApp.Models;
using WebApp.Services;
using WebApp.Tests.Fakes;

namespace WebApp.Tests;

/// <summary>
/// Unit tests for <see cref="ProductApiClient"/>, the single HTTP boundary of the WebApp.
/// A fake <see cref="HttpMessageHandler"/> records method, URI and body of every call,
/// so both the request contract and the response mapping are asserted without a server.
/// </summary>
public sealed class ProductApiClientTests
{
    private static (ProductApiClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        FakeHttpMessageHandler handler = new(responder);
        HttpClient httpClient = new(handler) { BaseAddress = StubResponses.BaseAddress };
        return (new ProductApiClient(httpClient), handler);
    }

    private static ProductViewModel SampleProduct() => new()
    {
        Id = 0,
        Name = "Test GPU",
        Description = "Unit-tested graphics card",
        ProductType = ProductType.EXTERNAL,
        Price = 120,
        Stock = 4
    };

    // ------------------------------------------------------------- GetAllAsync

    [Fact]
    public async Task GetAllAsync_WithoutFilters_RequestsProductsEndpoint()
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(_ => StubResponses.Json(
            new[] { new ProductViewModel { Id = 1, Name = "Ryzen 7 7800X3D", ProductType = ProductType.CPU, Price = 399, Stock = 25 } }));

        IReadOnlyList<ProductViewModel> products = await client.GetAllAsync();

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://productapi.test/api/products", request.RequestUri!.ToString());

        ProductViewModel product = Assert.Single(products);
        Assert.Equal("Ryzen 7 7800X3D", product.Name);
        Assert.Equal(399, product.Price);
        Assert.Equal(25, product.Stock);
    }

    [Fact]
    public async Task GetAllAsync_WithFilters_AddsSearchAndTypeToQueryString()
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(_ => StubResponses.Json(Array.Empty<ProductViewModel>()));

        _ = await client.GetAllAsync(search: "Dell", type: "MONITOR");

        Uri uri = handler.Requests.Single().RequestUri!;
        Assert.Equal("/api/products", uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("Dell", query["search"]);
        Assert.Equal("MONITOR", query["type"]);
    }

    [Theory]
    [InlineData(null, "MONITOR")]
    [InlineData("Dell", null)]
    public async Task GetAllAsync_WithSingleFilter_OnlyAddsThatParameter(string? search, string? type)
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(_ => StubResponses.Json(Array.Empty<ProductViewModel>()));

        _ = await client.GetAllAsync(search, type);

        Uri uri = handler.Requests.Single().RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(search is null ? null : "Dell", query["search"]);
        Assert.Equal(type is null ? null : "MONITOR", query["type"]);
    }

    [Fact]
    public async Task GetAllAsync_WithBlankFilters_OmitsQueryStringEntirely()
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(_ => StubResponses.Json(Array.Empty<ProductViewModel>()));

        _ = await client.GetAllAsync(search: "   ", type: string.Empty);

        Uri uri = handler.Requests.Single().RequestUri!;
        Assert.Equal("http://productapi.test/api/products", uri.ToString());
        Assert.True(string.IsNullOrEmpty(uri.Query));
    }

    [Fact]
    public async Task GetAllAsync_EncodesFilterValues()
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(_ => StubResponses.Json(Array.Empty<ProductViewModel>()));

        const string rawSearch = "a b&c=d";
        _ = await client.GetAllAsync(search: rawSearch, type: null);

        Uri uri = handler.Requests.Single().RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(rawSearch, query["search"]);
    }

    [Fact]
    public async Task GetAllAsync_WhenApiFails_ThrowsHttpRequestException()
    {
        (ProductApiClient client, _) = CreateClient(_ => StubResponses.Json(new { error = "boom" }, HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAllAsync());
    }

    // -------------------------------------------------------------- GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WhenProductExists_ReturnsMappedProduct()
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(request =>
            StubResponses.Json(new { id = 7, name = "Dell UltraSharp 27", productType = 1, price = 499, stock = 5 }));

        ProductViewModel? product = await client.GetByIdAsync(7);

        Assert.Equal("http://productapi.test/api/products/7", handler.Requests.Single().RequestUri!.ToString());
        Assert.NotNull(product);
        Assert.Equal(7, product.Id);
        Assert.Equal(ProductType.MONITOR, product.ProductType);
        Assert.Equal(5, product.Stock);
    }

    [Fact]
    public async Task GetByIdAsync_WhenApiReturnsNotFound_ReturnsNull()
    {
        (ProductApiClient client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await client.GetByIdAsync(999));
    }

    [Fact]
    public async Task GetByIdAsync_WhenApiFails_ThrowsHttpRequestException()
    {
        (ProductApiClient client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetByIdAsync(1));
    }

    // -------------------------------------------------------------- CreateAsync

    [Fact]
    public async Task CreateAsync_WithValidProduct_PostsJsonAndReportsSuccess()
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(_ => StubResponses.Json(SampleProduct(), HttpStatusCode.Created));

        (bool success, string? conflict) = await client.CreateAsync(SampleProduct());

        Assert.True(success);
        Assert.Null(conflict);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://productapi.test/api/products", request.RequestUri!.ToString());
        Assert.Contains("\"name\":\"Test GPU\"", handler.RequestBodies.Single(), StringComparison.Ordinal);
        Assert.Contains("\"stock\":4", handler.RequestBodies.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_WhenApiReportsConflict_ReturnsApiDetail()
    {
        (ProductApiClient client, _) = CreateClient(_ => StubResponses.Json(
            new { title = "Conflict", detail = "A product with the name 'Test GPU' already exists.", status = 409 },
            HttpStatusCode.Conflict));

        (bool success, string? conflict) = await client.CreateAsync(SampleProduct());

        Assert.False(success);
        Assert.Equal("A product with the name 'Test GPU' already exists.", conflict);
    }

    [Fact]
    public async Task CreateAsync_WhenConflictBodyIsNotJson_ReturnsNullDetail()
    {
        (ProductApiClient client, _) = CreateClient(_ => StubResponses.Text("<html>gateway error</html>", HttpStatusCode.Conflict));

        (bool success, string? conflict) = await client.CreateAsync(SampleProduct());

        Assert.False(success);
        Assert.Null(conflict);
    }

    // -------------------------------------------------------------- UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithValidProduct_PutsToIdEndpointAndReportsSuccess()
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        ProductViewModel product = SampleProduct();
        product.Id = 7;

        (bool success, string? conflict) = await client.UpdateAsync(product);

        Assert.True(success);
        Assert.Null(conflict);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("http://productapi.test/api/products/7", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task UpdateAsync_WhenProductIsMissing_ReturnsFailureWithoutDetail()
    {
        (ProductApiClient client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (bool success, string? conflict) = await client.UpdateAsync(SampleProduct());

        Assert.False(success);
        Assert.Null(conflict);
    }

    [Fact]
    public async Task UpdateAsync_WhenApiReportsConflict_ReturnsApiDetail()
    {
        (ProductApiClient client, _) = CreateClient(_ => StubResponses.Json(
            new { title = "Conflict", detail = "Name already taken.", status = 409 },
            HttpStatusCode.Conflict));

        (bool success, string? conflict) = await client.UpdateAsync(SampleProduct());

        Assert.False(success);
        Assert.Equal("Name already taken.", conflict);
    }

    // -------------------------------------------------------------- DeleteAsync

    [Fact]
    public async Task DeleteAsync_WhenProductExists_ReturnsTrue()
    {
        (ProductApiClient client, FakeHttpMessageHandler handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        Assert.True(await client.DeleteAsync(3));

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("http://productapi.test/api/products/3", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task DeleteAsync_WhenProductIsMissing_ReturnsFalse()
    {
        (ProductApiClient client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.False(await client.DeleteAsync(999));
    }

    [Fact]
    public async Task DeleteAsync_WhenApiFails_ThrowsHttpRequestException()
    {
        (ProductApiClient client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.DeleteAsync(1));
    }
}