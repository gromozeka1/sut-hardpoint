using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using WebApp.Controllers;
using WebApp.Models;
using WebApp.Services;
using WebApp.Tests.Fakes;

namespace WebApp.Tests;

/// <summary>
/// Unit tests for the MVC layer: action results, ModelState handling, TempData messages and
/// the graceful-degradation behaviour when the ProductAPI is unavailable.
/// <see cref="ProductApiClient"/> is backed by a fake handler, so no server is involved.
/// </summary>
public sealed class ProductsControllerTests
{
    private static (ProductsController Controller, FakeHttpMessageHandler Handler) CreateController(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        FakeHttpMessageHandler handler = new(responder);
        HttpClient httpClient = new(handler) { BaseAddress = StubResponses.BaseAddress };
        ProductApiClient apiClient = new(httpClient);

        ProductsController controller = new(apiClient, NullLogger<ProductsController>.Instance)
        {
            // TempData and ViewData are backed by the controller context, which is otherwise
            // null when a controller is constructed directly in a unit test.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        // TempData is normally supplied by MVC; in a unit test it must be provided explicitly.
        controller.TempData = new TempDataDictionary(controller.HttpContext, new InMemoryTempDataProvider());

        return (controller, handler);
    }

    /// <summary>
    /// Keeps TempData in memory for the duration of a single test.
    /// </summary>
    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = values;
    }

    private static ProductViewModel SampleProduct(int id = 1) => new()
    {
        Id = id,
        Name = "Ryzen 7 7800X3D",
        Description = "Gaming CPU",
        ProductType = ProductType.CPU,
        Price = 399,
        Stock = 25
    };

    // ------------------------------------------------------------------ Index

    [Fact]
    public async Task Index_WhenApiReturnsProducts_RendersThemAndPreservesFilters()
    {
        (ProductsController controller, FakeHttpMessageHandler handler) = CreateController(_ => StubResponses.Json(
            new[]
            {
                new { id = 1, name = "Ryzen 7 7800X3D", productType = 0, price = 399, stock = 25 },
                new { id = 2, name = "Dell UltraSharp 27", productType = 1, price = 499, stock = 5 }
            }));

        IActionResult result = await controller.Index(search: "Dell", type: "MONITOR", CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(result);
        var products = Assert.IsAssignableFrom<IEnumerable<ProductViewModel>>(view.Model);
        Assert.Equal(2, products.Count());
        Assert.Equal("Dell", view.ViewData["Search"]);
        Assert.Equal("MONITOR", view.ViewData["Type"]);
        Assert.Null(view.ViewData["ApiError"]);

        var query = System.Web.HttpUtility.ParseQueryString(handler.Requests.Single().RequestUri!.Query);
        Assert.Equal("Dell", query["search"]);
        Assert.Equal("MONITOR", query["type"]);
    }

    [Fact]
    public async Task Index_WhenApiIsUnavailable_RendersEmptyListWithErrorBanner()
    {
        (ProductsController controller, _) = CreateController(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        IActionResult result = await controller.Index(search: null, type: null, CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(result);
        var products = Assert.IsAssignableFrom<IEnumerable<ProductViewModel>>(view.Model);
        Assert.Empty(products);
        Assert.NotNull(view.ViewData["ApiError"]);
        Assert.Contains("Products could not be loaded", (string)view.ViewData["ApiError"]!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------ Create (GET + POST)

    [Fact]
    public void Create_Get_RendersEmptyForm()
    {
        (ProductsController controller, FakeHttpMessageHandler handler) = CreateController(_ => StubResponses.Json(new { }));

        IActionResult result = controller.Create();

        ViewResult view = Assert.IsType<ViewResult>(result);
        ProductViewModel model = Assert.IsType<ProductViewModel>(view.Model);
        Assert.Equal(string.Empty, model.Name);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Create_Post_WithInvalidModelState_DoesNotCallTheApi()
    {
        (ProductsController controller, FakeHttpMessageHandler handler) = CreateController(_ => StubResponses.Json(new { }));
        ProductViewModel product = SampleProduct();
        product.Name = string.Empty;
        controller.ModelState.AddModelError(nameof(ProductViewModel.Name), "The Name field is required.");

        IActionResult result = await controller.Create(product, CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.Same(product, view.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Create_Post_WhenNameConflicts_SurfacesApiDetailAsModelError()
    {
        (ProductsController controller, _) = CreateController(_ => StubResponses.Json(
            new { title = "Conflict", detail = "A product with the name 'Ryzen 7 7800X3D' already exists.", status = 409 },
            HttpStatusCode.Conflict));

        IActionResult result = await controller.Create(SampleProduct(), CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.NotNull(view.ViewData.ModelState);
        Assert.False(view.ViewData.ModelState.IsValid);
        string message = Assert.Single(view.ViewData.ModelState[string.Empty]!.Errors).ErrorMessage;
        Assert.Equal("A product with the name 'Ryzen 7 7800X3D' already exists.", message);
    }

    [Fact]
    public async Task Create_Post_WhenSuccessful_RedirectsAndSetsSuccessMessage()
    {
        (ProductsController controller, _) = CreateController(_ => StubResponses.Json(SampleProduct(), HttpStatusCode.Created));

        IActionResult result = await controller.Create(SampleProduct(), CancellationToken.None);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductsController.Index), redirect.ActionName);
        Assert.Equal("Product created successfully.", controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task Create_Post_WhenApiIsUnavailable_SurfacesGenericModelError()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        IActionResult result = await controller.Create(SampleProduct(), CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(result);
        string message = Assert.Single(view.ViewData.ModelState[string.Empty]!.Errors).ErrorMessage;
        Assert.Equal("The product could not be created. Check the Product API and try again.", message);
    }

    // ------------------------------------------------------------------- Edit

    [Fact]
    public async Task Edit_Get_WhenProductExists_RendersIt()
    {
        (ProductsController controller, _) = CreateController(_ => StubResponses.Json(SampleProduct(id: 7)));

        IActionResult result = await controller.Edit(7, CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(result);
        ProductViewModel model = Assert.IsType<ProductViewModel>(view.Model);
        Assert.Equal(7, model.Id);
    }

    [Fact]
    public async Task Edit_Get_WhenProductIsMissing_ReturnsNotFound()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.IsType<NotFoundResult>(await controller.Edit(999, CancellationToken.None));
    }

    [Fact]
    public async Task Edit_Get_WhenApiIsUnavailable_Returns503()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        ObjectResult result = Assert.IsType<ObjectResult>(await controller.Edit(1, CancellationToken.None));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    [Fact]
    public async Task Edit_Post_WhenRouteIdDiffersFromBody_ReturnsBadRequestWithoutCallingApi()
    {
        (ProductsController controller, FakeHttpMessageHandler handler) = CreateController(_ => StubResponses.Json(SampleProduct()));

        IActionResult result = await controller.Edit(2, SampleProduct(id: 1), CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Edit_Post_WithInvalidModelState_RendersFormWithoutCallingApi()
    {
        (ProductsController controller, FakeHttpMessageHandler handler) = CreateController(_ => StubResponses.Json(SampleProduct()));
        controller.ModelState.AddModelError(nameof(ProductViewModel.Price), "The field Price must be between 0 and int.MaxValue.");

        IActionResult result = await controller.Edit(1, SampleProduct(), CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Edit_Post_WhenNameConflicts_SurfacesApiDetailAsModelError()
    {
        (ProductsController controller, _) = CreateController(_ => StubResponses.Json(
            new { title = "Conflict", detail = "Name already taken.", status = 409 },
            HttpStatusCode.Conflict));

        IActionResult result = await controller.Edit(1, SampleProduct(), CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(result);
        string message = Assert.Single(view.ViewData.ModelState[string.Empty]!.Errors).ErrorMessage;
        Assert.Equal("Name already taken.", message);
    }

    [Fact]
    public async Task Edit_Post_WhenProductDisappeared_ReturnsNotFound()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        IActionResult result = await controller.Edit(1, SampleProduct(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_WhenSuccessful_RedirectsAndSetsSuccessMessage()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        IActionResult result = await controller.Edit(1, SampleProduct(), CancellationToken.None);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductsController.Index), redirect.ActionName);
        Assert.Equal("Product updated successfully.", controller.TempData["SuccessMessage"]);
    }

    // ---------------------------------------------------------------- Details

    [Fact]
    public async Task Details_WhenProductExists_RendersIt()
    {
        (ProductsController controller, _) = CreateController(_ => StubResponses.Json(SampleProduct(id: 3)));

        ViewResult view = Assert.IsType<ViewResult>(await controller.Details(3, CancellationToken.None));
        Assert.Equal(3, Assert.IsType<ProductViewModel>(view.Model).Id);
    }

    [Fact]
    public async Task Details_WhenProductIsMissing_ReturnsNotFound()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.IsType<NotFoundResult>(await controller.Details(999, CancellationToken.None));
    }

    [Fact]
    public async Task Details_WhenApiIsUnavailable_Returns503()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        ObjectResult result = Assert.IsType<ObjectResult>(await controller.Details(1, CancellationToken.None));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    // ------------------------------------------------------- Delete (GET/POST)

    [Fact]
    public async Task Delete_Get_WhenProductExists_RendersConfirmation()
    {
        (ProductsController controller, _) = CreateController(_ => StubResponses.Json(SampleProduct(id: 3)));

        ViewResult view = Assert.IsType<ViewResult>(await controller.Delete(3, CancellationToken.None));
        Assert.Equal(3, Assert.IsType<ProductViewModel>(view.Model).Id);
    }

    [Fact]
    public async Task Delete_Get_WhenProductIsMissing_ReturnsNotFound()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.IsType<NotFoundResult>(await controller.Delete(999, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Get_WhenApiIsUnavailable_Returns503()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        ObjectResult result = Assert.IsType<ObjectResult>(await controller.Delete(1, CancellationToken.None));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    [Fact]
    public async Task DeleteConfirmed_WhenProductExists_RedirectsWithSuccessMessage()
    {
        (ProductsController controller, FakeHttpMessageHandler handler) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        IActionResult result = await controller.DeleteConfirmed(3, CancellationToken.None);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductsController.Index), redirect.ActionName);
        Assert.Equal("Product deleted successfully.", controller.TempData["SuccessMessage"]);
        Assert.Equal(HttpMethod.Delete, handler.Requests.Single().Method);
    }

    [Fact]
    public async Task DeleteConfirmed_WhenProductIsMissing_ReturnsNotFound()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.IsType<NotFoundResult>(await controller.DeleteConfirmed(999, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteConfirmed_WhenApiIsUnavailable_RedirectsWithErrorMessage()
    {
        (ProductsController controller, _) = CreateController(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        IActionResult result = await controller.DeleteConfirmed(3, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("The product could not be deleted. Check the Product API and try again.", controller.TempData["ErrorMessage"]);
    }
}