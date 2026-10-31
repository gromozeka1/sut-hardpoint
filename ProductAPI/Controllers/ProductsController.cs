using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAPI.Models;

namespace ProductAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ProductContext _context;

    public ProductsController(ProductContext context)
    {
        _context = context;
    }

    // GET: api/Products?search=ryzen&type=CPU
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> GetProduct(
        [FromQuery] string? search,
        [FromQuery] string? type)
    {
        IQueryable<Product> query = _context.Product;

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Requirement 4.2: case-insensitive substring match on Name.
            // SQLite's LIKE/instr comparisons are case-sensitive, so fold both sides first
            // (the same technique the duplicate-name checks below rely on).
            string term = search.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<ProductType>(type, ignoreCase: true, out ProductType productType))
            {
                return BadRequest($"Unknown product type '{type}'.");
            }

            query = query.Where(p => p.ProductType == productType);
        }

        return await query.ToListAsync();
    }

    // GET: api/Products/5
    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> GetProduct(int id)
    {
        Product? product = await _context.Product.FindAsync(id);

        if (product == null)
        {
            return NotFound();
        }

        return product;
    }

    // PUT: api/Products/5
    [HttpPut("{id}")]
    public async Task<IActionResult> PutProduct(int id, Product product)
    {
        if (id != product.Id)
        {
            return BadRequest();
        }

        bool nameConflict = await _context.Product
            .AnyAsync(p => p.Name.ToLower() == product.Name.ToLower() && p.Id != id);

        if (nameConflict)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = $"A product with the name '{product.Name}' already exists.",
                Status = StatusCodes.Status409Conflict
            });
        }

        _context.Entry(product).State = EntityState.Modified;

        try
        {
            _ = await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ProductExists(id))
            {
                return NotFound();
            }

            throw;
        }

        return NoContent();
    }

    // POST: api/Products
    [HttpPost]
    public async Task<ActionResult<Product>> PostProduct(Product product)
    {
        bool nameConflict = await _context.Product
            .AnyAsync(p => p.Name.ToLower() == product.Name.ToLower());

        if (nameConflict)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = $"A product with the name '{product.Name}' already exists.",
                Status = StatusCodes.Status409Conflict
            });
        }

        _ = _context.Product.Add(product);
        _ = await _context.SaveChangesAsync();

        return CreatedAtAction("GetProduct", new { id = product.Id }, product);
    }

    // DELETE: api/Products/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        Product? product = await _context.Product.FindAsync(id);

        if (product == null)
        {
            return NotFound();
        }

        _ = _context.Product.Remove(product);
        _ = await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool ProductExists(int id)
    {
        return _context.Product.Any(e => e.Id == id);
    }
}