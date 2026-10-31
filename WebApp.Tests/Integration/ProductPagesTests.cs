using System.Globalization;
using System.Net;
using WebApp.Models;

namespace WebApp.Tests.Integration;

/// <summary>
/// End-to-end tests for the MVC pipeline: routing, model binding, antiforgery, TempData,
/// redirects and Razor rendering all run for real on the in-memory <c>TestServer</c>.
/// <see cref="ProductApiStub"/> answers every call the typed <c>ProductApiClient</c> makes,
/// so nothing here reaches a socket and each test starts from an empty product list.
/// </summary>
/// <remarks>
/// These are the tests that cover what the unit suite cannot: the <c>.cshtml</c> views only
/// execute through an HTTP request, and asserts here are made against the rendered HTML.
/// </remarks>
public sealed class ProductPagesTests : IntegrationTestBase
{
    // ------------------------------------------------------------------ Index

    [Fact]
    public async Task Index_RendersEveryProductWithItsTypePriceAndStockBadge()
    {
        _ = Api.Add("Ryzen 7 7800X3D", price: 399, stock: 25, description: "Gaming CPU", productType: ProductType.CPU);
        _ = Api.Add("Logitech MX Master 3S", price: 99, stock: 0, description: "Wireless mouse", productType: ProductType.PERIPHERALS);
        _ = Api.Add("Dell UltraSharp 27", price: 499, stock: 5, description: "27-inch monitor", productType: ProductType.MONITOR);

        string html = await GetHtmlAsync("/");

        // The layout and the page heading come from _Layout.cshtml and Index.cshtml.
        Assert.Contains("<title>Products - WebApp</title>", html);
        Assert.Contains("<h1>Products</h1>", html);
        Assert.Contains("href=\"/Home/Privacy\"", html);

        // Every product, with the display names from ProductType's [Display] attributes.
        Assert.Contains("Ryzen 7 7800X3D", html);
        Assert.Contains("Gaming CPU", html);
        Assert.Contains("Peripherals", html);
        Assert.Contains("Monitor", html);
        Assert.Contains("href=\"/Products/Details/1\"", html);

        // The three _StockBadge branches: >= 10, 1-9 and 0.
        Assert.Contains("25 in stock", html);
        Assert.Contains("Low stock", html);
        Assert.Contains("Out of stock", html);

        Assert.Contains("GET /api/products", Api.Requests);
    }

    [Fact]
    public async Task Index_WithNoProducts_RendersTheEmptyStateInsteadOfATable()
    {
        string html = await GetHtmlAsync("/");

        Assert.Contains("No products found. Add a product to get started.", html);
        Assert.DoesNotContain("<tbody>", html);
    }

    [Fact]
    public async Task Index_WithFilters_EchoesThemBackIntoTheFormAndForwardsThemToTheApi()
    {
        _ = Api.Add("Dell UltraSharp 27", price: 499, stock: 5, description: "27-inch monitor", productType: ProductType.MONITOR);
        _ = Api.Add("Ryzen 7 7800X3D", price: 399, stock: 25, description: "Gaming CPU", productType: ProductType.CPU);

        string html = await GetHtmlAsync("/?search=dell&type=MONITOR");

        Assert.Equal("dell", FindInputValue(html, "search"));
        Assert.Equal("MONITOR", FindSelectedOptionValue(html));
        Assert.Contains("Dell UltraSharp 27", html);
        Assert.DoesNotContain("Ryzen 7 7800X3D", html);

        string request = Assert.Single(Api.Requests);
        Assert.Contains("search=dell", request);
        Assert.Contains("type=MONITOR", request);
    }

    [Fact]
    public async Task Index_WhenTheApiIsDown_RendersTheErrorBannerInsteadOfFailing()
    {
        Api.Unreachable = true;

        string html = await GetHtmlAsync("/");

        Assert.Contains("Products could not be loaded. Check that the Product API is running and try again.", html);
        Assert.DoesNotContain("<tbody>", html);
        // The banner replaces the empty state, so the page does not claim the catalogue is empty.
        Assert.DoesNotContain("No products found.", html);
    }

    // -------------------------------------------------------- Create (GET/POST)

    [Fact]
    public async Task Create_Get_RendersAnEmptyFormWithTheEnumOptionsAndAnAntiforgeryToken()
    {
        string html = await GetHtmlAsync("/Products/Create");

        Assert.Contains("<title>Add product - WebApp</title>", html);
        Assert.Contains("<h1>Add product</h1>", html);
        Assert.True(string.IsNullOrEmpty(FindInputValue(html, "Name")));
        // A default ProductViewModel has the enum's zero value, so CPU is preselected.
        Assert.Equal("0", FindSelectedOptionValue(html));

        // GetEnumSelectList renders the [Display] names, not the enum identifiers.
        Assert.Contains(">CPU</option>", html);
        Assert.Contains(">Monitor</option>", html);
        Assert.Contains(">Peripherals</option>", html);
        Assert.Contains(">External</option>", html);

        Assert.Contains(">Create product</button>", html);
        Assert.NotEmpty(ExtractAntiForgeryToken(html));
        Assert.Empty(Api.Requests);
    }

    [Fact]
    public async Task Create_Post_WithAValidProduct_RedirectsToTheListAndShowsTheSuccessMessage()
    {
        string token = await FetchTokenAsync(RedirectClient, "/Products/Create");

        using HttpResponseMessage response = await PostFormAsync(
            RedirectClient,
            "/Products/Create",
            ProductForm(token, NewProduct()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Products/Index are the route defaults, so link generation yields the shortest URL.
        Assert.Equal("/", response.Headers.Location?.ToString());
        Assert.Equal("POST /api/products", Assert.Single(Api.Requests));

        string html = await GetHtmlAsync(RedirectClient, "/Products");
        Assert.Contains("Product created successfully.", html);
        Assert.Contains("Mechanical Keyboard", html);
        Assert.Contains("12 in stock", html);
    }

    [Fact]
    public async Task Create_Post_WithAMissingName_RedisplaysTheFormWithoutCallingTheApi()
    {
        ProductViewModel product = NewProduct();
        product.Name = string.Empty;
        product.Description = "Keep me";

        string token = await FetchTokenAsync(Client, "/Products/Create");
        using HttpResponseMessage response = await PostFormAsync("/Products/Create", ProductForm(token, product));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        // The field error renders in the span, not only in the data-val-required attribute the
        // GET page already carries.
        Assert.Matches("field-validation-error[^>]*>\\s*The Name field is required\\.", html);
        // Model binding round-trips the submitted values back into the form.
        Assert.Matches(">\\s*Keep me\\s*</textarea>", html);
        Assert.DoesNotContain(Api.Requests, request => request.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_Post_WhenTheApiReportsADuplicateName_ShowsTheApiDetail()
    {
        _ = Api.Add("Existing Product", price: 10, stock: 1);

        ProductViewModel product = NewProduct();
        product.Name = "Existing Product";

        string token = await FetchTokenAsync(Client, "/Products/Create");
        using HttpResponseMessage response = await PostFormAsync("/Products/Create", ProductForm(token, product));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Add product", html);
        Assert.Contains("Existing Product", html);
        Assert.Contains("already exists.", html);
        Assert.Single(Api.Products);
    }

    [Fact]
    public async Task Create_Post_WithoutAnAntiforgeryToken_IsRejectedByThePipeline()
    {
        using HttpResponseMessage response = await PostFormAsync(
            "/Products/Create",
            new Dictionary<string, string> { ["Name"] = "Forged Product" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Api.Requests);
    }

    // ---------------------------------------------------------- Edit (GET/POST)

    [Fact]
    public async Task Edit_Get_RendersTheFormPrefilledFromTheApi()
    {
        StubProduct product = Api.Add(
            "Ryzen 7 7800X3D",
            price: 399,
            stock: 25,
            description: "Gaming CPU",
            productType: ProductType.CPU);

        string html = await GetHtmlAsync($"/Products/Edit/{product.Id}");

        Assert.Contains("<title>Edit product - WebApp</title>", html);
        Assert.Equal(product.Id.ToString(CultureInfo.InvariantCulture), FindInputValue(html, "Id"));
        Assert.Equal("Ryzen 7 7800X3D", FindInputValue(html, "Name"));
        Assert.Equal("399", FindInputValue(html, "Price"));
        Assert.Equal("25", FindInputValue(html, "Stock"));
        Assert.Matches(">\\s*Gaming CPU\\s*</textarea>", html);
        // GetEnumSelectList values are the numeric enum values; CPU is 0.
        Assert.Equal("0", FindSelectedOptionValue(html));
        Assert.Contains(">Save changes</button>", html);
    }

    [Fact]
    public async Task Edit_Post_WithValidChanges_RedirectsAndPersistsThemThroughTheApi()
    {
        StubProduct product = Api.Add("Ryzen 7 7800X3D", price: 399, stock: 25, description: "Gaming CPU");

        string token = await FetchTokenAsync(RedirectClient, $"/Products/Edit/{product.Id}");
        ProductViewModel edited = new()
        {
            Id = product.Id,
            Name = "Ryzen 9 9900X3D",
            Description = "Faster CPU",
            ProductType = ProductType.CPU,
            Price = 599,
            Stock = 0
        };

        using HttpResponseMessage response = await PostFormAsync(
            RedirectClient,
            $"/Products/Edit/{product.Id}",
            ProductForm(token, edited));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
        Assert.Equal("Ryzen 9 9900X3D", product.Name);
        Assert.Equal(599, product.Price);
        Assert.Equal(0, product.Stock);

        string html = await GetHtmlAsync(RedirectClient, "/Products");
        Assert.Contains("Product updated successfully.", html);
        Assert.Contains("Ryzen 9 9900X3D", html);
    }

    [Fact]
    public async Task Edit_Post_WhenTheApiReportsAConflict_RedisplaysTheFormWithTheApiDetail()
    {
        StubProduct product = Api.Add("First Product", price: 100, stock: 5);
        _ = Api.Add("Second Product", price: 200, stock: 7, productType: ProductType.MONITOR);

        string token = await FetchTokenAsync(Client, $"/Products/Edit/{product.Id}");
        ProductViewModel conflicting = new()
        {
            Id = product.Id,
            Name = "Second Product",
            Description = "Renamed",
            ProductType = ProductType.CPU,
            Price = 100,
            Stock = 5
        };

        using HttpResponseMessage response = await PostFormAsync(
            Client,
            $"/Products/Edit/{product.Id}",
            ProductForm(token, conflicting));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<h1>Edit product</h1>", html);
        // The apostrophes in the API detail are HTML-encoded by Razor, so match the stable tail.
        Assert.Contains("already exists.", html);
        Assert.Contains("Second Product", html);
        // The stub rejected the rename: the first product keeps its name.
        Assert.Equal("First Product", product.Name);
    }

    // --------------------------------------------------------------- Details

    [Fact]
    public async Task Details_RendersEveryFieldIncludingTheStockBadge()
    {
        StubProduct product = Api.Add(
            "Dell UltraSharp 27",
            price: 499,
            stock: 5,
            description: "27-inch monitor",
            productType: ProductType.MONITOR);

        string html = await GetHtmlAsync($"/Products/Details/{product.Id}");

        Assert.Contains("<title>Product details - WebApp</title>", html);
        Assert.Contains("<h1>Product details</h1>", html);
        Assert.Contains(">Id</dt>", html);
        Assert.Contains(">Name</dt>", html);
        Assert.Contains(">Description</dt>", html);
        // The label comes from the [Display] attribute on ProductViewModel.ProductType.
        Assert.Contains(">Product type</dt>", html);
        Assert.Contains($">{product.Id}</dd>", html);
        Assert.Contains(">Dell UltraSharp 27</dd>", html);
        Assert.Contains(">27-inch monitor</dd>", html);
        Assert.Contains(">Monitor</dd>", html);
        Assert.Contains(">499</dd>", html);
        Assert.Contains("Low stock", html);
        Assert.Contains("href=\"/Products/Edit/1\"", html);
    }

    [Fact]
    public async Task Details_WhenTheProductDoesNotExist_ReturnsNotFoundThroughThePipeline()
    {
        _ = await GetStringAsync("/Products/Details/999", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Details_WhenTheApiIsUnavailable_Returns503()
    {
        Api.Unreachable = true;

        string body = await GetStringAsync("/Products/Details/1", HttpStatusCode.ServiceUnavailable);

        Assert.Contains("The Product API is unavailable.", body);
    }

    // ---------------------------------------------------------------- Delete

    [Fact]
    public async Task Delete_Get_RendersTheConfirmationWithTheProductAndAForm()
    {
        StubProduct product = Api.Add(
            "Throwaway Mouse",
            price: 49,
            stock: 0,
            productType: ProductType.PERIPHERALS);

        string html = await GetHtmlAsync($"/Products/Delete/{product.Id}");

        Assert.Contains("<title>Delete product - WebApp</title>", html);
        Assert.Contains("Are you sure you want to delete this product?", html);
        Assert.Contains(">Throwaway Mouse</dd>", html);
        Assert.Contains(">Peripherals</dd>", html);
        Assert.Contains(">49</dd>", html);
        Assert.Equal(product.Id.ToString(CultureInfo.InvariantCulture), FindInputValue(html, "Id"));
        Assert.Contains(">Delete</button>", html);
        Assert.NotEmpty(ExtractAntiForgeryToken(html));
    }

    [Fact]
    public async Task Delete_Post_RemovesTheProductAndRedirectsWithTheSuccessMessage()
    {
        StubProduct product = Api.Add("Throwaway Mouse", price: 49, stock: 0);

        string token = await FetchTokenAsync(RedirectClient, $"/Products/Delete/{product.Id}");
        using HttpResponseMessage response = await PostFormAsync(
            RedirectClient,
            $"/Products/Delete/{product.Id}",
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Id"] = product.Id.ToString(CultureInfo.InvariantCulture)
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
        Assert.Empty(Api.Products);
        Assert.Contains($"DELETE /api/products/{product.Id}", Api.Requests);

        string html = await GetHtmlAsync(RedirectClient, "/Products");
        Assert.Contains("Product deleted successfully.", html);
        Assert.Contains("No products found. Add a product to get started.", html);
    }

    // ----------------------------------------------------------------- Error

    [Fact]
    public async Task ErrorPage_RendersTheTraceIdentifierAsTheRequestId()
    {
        string html = await GetHtmlAsync("/Home/Error");

        Assert.Contains("<title>Error - WebApp</title>", html);
        Assert.Contains("An error occurred while processing your request.", html);
        // ShowRequestId is only true when the trace identifier is present.
        Assert.Contains("Request ID:", html);
        Assert.Contains("<code>", html);
    }

    // ---------------------------------------------------------------- helpers

    private static ProductViewModel NewProduct() => new()
    {
        Name = "Mechanical Keyboard",
        Description = "Tactile switches",
        ProductType = ProductType.PERIPHERALS,
        Price = 89,
        Stock = 12
    };

    /// <summary>
    /// Loads a form page and returns the antiforgery token it embeds. The page must be GET-ed
    /// through the same client used for the POST, because that GET is also what plants the
    /// antiforgery cookie the token is validated against.
    /// </summary>
    private async Task<string> FetchTokenAsync(HttpClient client, string url)
        => ExtractAntiForgeryToken(await GetHtmlAsync(client, url));

    private static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Builds the urlencoded payload a browser would submit from <c>_ProductForm</c>: every
    /// bound field plus the hidden antiforgery token.
    /// </summary>
    private static Dictionary<string, string> ProductForm(string antiforgeryToken, ProductViewModel product) => new(StringComparer.Ordinal)
    {
        ["__RequestVerificationToken"] = antiforgeryToken,
        ["Id"] = product.Id.ToString(CultureInfo.InvariantCulture),
        ["Name"] = product.Name,
        ["Description"] = product.Description ?? string.Empty,
        ["ProductType"] = product.ProductType.ToString(),
        ["Price"] = product.Price.ToString(CultureInfo.InvariantCulture),
        ["Stock"] = product.Stock.ToString(CultureInfo.InvariantCulture)
    };
}