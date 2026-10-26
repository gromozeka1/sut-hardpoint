using Microsoft.AspNetCore.Mvc;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Controllers;

public class ProductsController : Controller
{
    private readonly ProductApiClient _productApi;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(ProductApiClient productApi, ILogger<ProductsController> logger)
    {
        _productApi = productApi;
        _logger = logger;
    }

    public async Task<IActionResult> Index(
        string? search,
        string? type,
        CancellationToken cancellationToken)
    {
        ViewBag.Search = search;
        ViewBag.Type = type;

        try
        {
            return View(await _productApi.GetAllAsync(search, type, cancellationToken));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Unable to load products from the Product API.");
            ViewBag.ApiError = "Products could not be loaded. Check that the Product API is running and try again.";
            return View(Array.Empty<ProductViewModel>());
        }
    }

    public IActionResult Create()
    {
        return View(new ProductViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductViewModel product, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(product);
        }

        try
        {
            (bool success, string? conflict) = await _productApi.CreateAsync(product, cancellationToken);

            if (!success)
            {
                ModelState.AddModelError(string.Empty, conflict ?? "A product with this name already exists.");
                return View(product);
            }

            TempData["SuccessMessage"] = "Product created successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Unable to create product {ProductName}.", product.Name);
            ModelState.AddModelError(string.Empty, "The product could not be created. Check the Product API and try again.");
            return View(product);
        }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        try
        {
            ProductViewModel? product = await _productApi.GetByIdAsync(id, cancellationToken);
            return product is null ? NotFound() : View(product);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Unable to load product {ProductId} for editing.", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "The Product API is unavailable.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductViewModel product, CancellationToken cancellationToken)
    {
        if (id != product.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(product);
        }

        try
        {
            (bool success, string? conflict) = await _productApi.UpdateAsync(product, cancellationToken);

            if (!success && conflict is not null)
            {
                ModelState.AddModelError(string.Empty, conflict);
                return View(product);
            }

            if (!success)
            {
                return NotFound();
            }

            TempData["SuccessMessage"] = "Product updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Unable to update product {ProductId}.", product.Id);
            ModelState.AddModelError(string.Empty, "The product could not be updated. Check the Product API and try again.");
            return View(product);
        }
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        try
        {
            ProductViewModel? product = await _productApi.GetByIdAsync(id, cancellationToken);
            return product is null ? NotFound() : View(product);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Unable to load product {ProductId}.", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "The Product API is unavailable.");
        }
    }

    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            ProductViewModel? product = await _productApi.GetByIdAsync(id, cancellationToken);
            return product is null ? NotFound() : View(product);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Unable to load product {ProductId} for deletion.", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "The Product API is unavailable.");
        }
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _productApi.DeleteAsync(id, cancellationToken))
            {
                return NotFound();
            }

            TempData["SuccessMessage"] = "Product deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Unable to delete product {ProductId}.", id);
            TempData["ErrorMessage"] = "The product could not be deleted. Check the Product API and try again.";
            return RedirectToAction(nameof(Index));
        }
    }
}
