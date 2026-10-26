# Design Document

## Overview

This design covers five features that add data validation, a stock quantity field, search/filter, and duplicate-name prevention to the ProductAPI + WebApp SUT. The implementation is layered: API model and controller changes come first, followed by WebApp service/controller/view changes, and finally test layer additions. No authentication or pagination is introduced.

The two services communicate at runtime only; `Tests` and `WebApp` have no compile-time reference to `ProductAPI`. Models are mirrored independently in each project.

## Architecture

```
Browser
  │  GET /Products?search=ryzen&type=CPU
  ▼
WebApp (ASP.NET Core MVC — port 5168)
  ProductsController.Index(search, type)
    │  HTTP GET http://productapi:8080/api/products?search=ryzen&type=CPU
    ▼
  ProductApiClient (typed HttpClient)
  │
  ▼
ProductAPI (ASP.NET Core Web API — port 5044)
  ProductsController.GetProduct(search, type)
    │  EF Core LINQ → SQLite
    ▼
  ProductContext / Product table
```

The filter state round-trips through `ViewBag` so the search form repopulates after submission. No caching layer is introduced.

## Components and Interfaces

### ProductAPI — Model (`ProductAPI/Models/Product.cs`)

**Current:**
```csharp
public class Product
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public ProductType ProductType { get; set; }
    public int Price { get; set; }
}
```

**After:**
```csharp
public class Product
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    public ProductType ProductType { get; set; }

    [Range(0, int.MaxValue)]
    public int Price { get; set; }

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }
}
```

The `[ApiController]` attribute on `ProductsController` activates automatic model validation, so annotations alone enforce 400-level responses without extra controller code.

### ProductAPI — Controller (`ProductAPI/Controllers/ProductsController.cs`)

**`GET api/products`** — add `[FromQuery] string? search` and `[FromQuery] string? type`. Build an `IQueryable<Product>` and apply `Where` clauses before `ToListAsync`. Return `400` for an unrecognised `type` value.

**`POST api/products`** — replace the `ProductExists(product.Id)` check (which produced 500) with a case-insensitive name-uniqueness check using `AnyAsync`. Return `409 Conflict` with a `ProblemDetails` body on collision.

**`PUT api/products/{id}`** — add a name-uniqueness check excluding the product being updated. Return `409 Conflict` on collision using the same `ProblemDetails` shape.

### ProductAPI — Database (`ProductAPI/Models/ProductContext.cs`)

`OnModelCreating` seed data gains explicit `Stock` values (Id 1 → 25, Id 2 → 5, Id 3 → 0) covering all three badge states.

### ProductAPI — Migrations

The project currently uses `EnsureCreated` and has no `Migrations/` folder. Two migrations are needed:

1. `InitialCreate` — baselines the existing schema.
2. `AddStockToProduct` — adds `Stock INTEGER NOT NULL DEFAULT 0`.

`Program.cs` is updated to call `Database.Migrate()` instead of `Database.EnsureCreated()`.

### WebApp — Model (`WebApp/Models/ProductViewModel.cs`)

Add `[Range(0, int.MaxValue)] public int Stock { get; set; }`. The `ProductType` enum spelling (`PERIPHERALS`) is unchanged.

### WebApp — Service (`WebApp/Services/ProductApiClient.cs`)

**`GetAllAsync`** gains optional `string? search` and `string? type` parameters. The method conditionally appends a query string to the endpoint URL.

**`CreateAsync` and `UpdateAsync`** change return type to surface `409 Conflict` as a structured result (success flag + optional conflict message) rather than throwing an `HttpRequestException`. The `ProblemDetails.Detail` field is deserialised from the response body.

### WebApp — Controller (`WebApp/Controllers/ProductsController.cs`)

**`Index`** action gains `string? search` and `string? type` parameters, sets `ViewBag.Search`/`ViewBag.Type`, and passes them to `ProductApiClient.GetAllAsync`.

**`Create` and `Edit` POST actions** consume the new structured result from `ProductApiClient`. On conflict, they call `ModelState.AddModelError` and re-render the form.

### WebApp — Views

| View | Change |
|------|--------|
| `Views/Shared/_Layout.cshtml` | Render `_ValidationScriptsPartial` before `Scripts` section |
| `Views/Products/_ProductForm.cshtml` | Add `Stock` number input with label and validation span |
| `Views/Shared/_StockBadge.cshtml` (new) | Razor partial, model `int`; renders Bootstrap badge by stock level |
| `Views/Products/Index.cshtml` | Add search form above table; add Stock column using `_StockBadge` partial |
| `Views/Products/Details.cshtml` | Add Stock `<dt>/<dd>` pair using `_StockBadge` partial |

### Tests — Models (`Tests/Models/Product.cs`)

Add `public int Stock { get; set; }`. `ProductType` enum retains `PERIPHARALS` spelling to match the API.

### Tests — Support (`Tests/Support/ProductsApi.cs`)

- Update `CreateAsync` to serialise `product.Stock` in the request body.
- Add `GetAllAsync(string? search, string? type)` overload that builds the query string and returns `IAPIResponse`.

### Tests — Pages

**`ProductPage.cs`** — add `TxtStock` locator (`Page.Locator("#Stock")`); include `Stock` fill in `CreateProduct`.

**`ProductListPage.cs`** — add `TxtSearch`, `SelectTypeFilter`, `BtnSearch` locators; add `SearchAsync` method; add `StockBadgeForProduct` locator.

### Tests — Steps

**`ProductApiSteps.cs`** — add step bindings for: 400/409 validation scenarios, conflict detail assertion, search/filter scenarios, Stock field assertion.

**`ProductSteps.cs`** — add step bindings for: Stock field fill, stock badge assertion, search form interaction, filtered list assertion, client-side validation message assertion.

## Data Models

### Product — canonical shape (after changes)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `int` | PK, auto-increment | |
| `Name` | `string` | Required, max 100 chars, unique (case-insensitive) | Non-nullable after this change |
| `Description` | `string?` | Optional, max 1 000 chars | |
| `ProductType` | `ProductType` enum | CPU / MONITOR / PERIPHARALS / EXTERNAL | Stored as int in SQLite |
| `Price` | `int` | ≥ 0 | |
| `Stock` | `int` | ≥ 0, default 0 | New column; SQLite default 0 |

### Seed data (after Stock update)

| Id | Name | ProductType | Price | Stock |
|----|------|-------------|-------|-------|
| 1 | Ryzen 7 7800X3D | CPU | 399 | 25 |
| 2 | Dell UltraSharp 27 | MONITOR | 499 | 5 |
| 3 | Logitech MX Master 3S | PERIPHARALS | 99 | 0 |

Stock values 25, 5, and 0 cover the three badge states (≥ 10, < 10, = 0) for the existing `@api` scenario "Get an existing product" (id = 1, stock = 25).

## Error Handling

### API error responses

| Situation | HTTP status | Body |
|-----------|-------------|------|
| Missing/empty Name, Name > 100, Price < 0, Stock < 0 | 400 Bad Request | `ValidationProblemDetails` (automatic via `[ApiController]`) |
| Duplicate name on POST | 409 Conflict | `ProblemDetails { title: "Conflict", detail: "A product with the name '...' already exists." }` |
| Duplicate name on PUT | 409 Conflict | Same as above |
| Unrecognised `type` query param | 400 Bad Request | Explicit `BadRequest()` return |
| Id not found on GET/PUT/DELETE | 404 Not Found | Default MVC response |

### WebApp error handling

`ProductApiClient.CreateAsync` and `UpdateAsync` no longer call `EnsureSuccessStatusCode` unconditionally. They return a result type that the controller inspects:

- `200–299` → success path, redirect to Index.
- `409` → read `ProblemDetails.Detail`, add `ModelState` error, re-render form.
- Other non-success → throw `HttpRequestException` (caught by existing try/catch in controller, logs error, adds generic `ModelState` error).

### Existing GET error handling

The existing `HttpRequestException` catch blocks in `ProductsController` (WebApp) for `Index`, `Edit`, `Details`, and `Delete` are unchanged.

## Testing Strategy

### API test scenarios (new, in `ProductApi.feature`)

| Scenario | Requirement |
|----------|-------------|
| POST with missing Name → 400 | 1.2 |
| POST with Name > 100 chars → 400 | 1.3 |
| POST with negative Price → 400 | 1.4 |
| POST duplicate name → 409 | 5.1, 5.2 |
| PUT duplicate name → 409 | 5.3 |
| PUT same name on same product → 204 | 5.4 |
| GET with search filter | 4.2 |
| GET with type filter | 4.3 |
| GET with combined filters | 4.4 |
| GET with invalid type → 400 | 4.3 |
| Stock field persisted and returned | 3.8, 3.9 |

### UI test scenarios (new, in `Product.feature`)

| Scenario | Requirement |
|----------|-------------|
| Create product with Stock ≥ 10 → green badge | 3.6 |
| Create product with Stock = 0 → "Out of stock" badge | 3.6 |
| Create product with Stock < 10 → "Low stock" badge | 3.6 |
| Submit empty Name → inline validation error | 2.2 |
| Submit duplicate name → form re-renders with error | 5.5 |
| Search by name filters list | 4.6–4.8 |
| Filter by type filters list | 4.6–4.8 |

### Existing test compatibility

The existing `@api` scenario "Get an existing product" (seed id = 1, price = 399) is unaffected. If it is later updated to assert `Stock`, the expected value is `25`. The existing `@ui` "Create product and verify the details" scenario must have its DataTable updated to include a `Stock` column.

## Correctness Properties

Property 1: Uniqueness invariant — at no point should the `Product` table contain two rows with the same `Name` (case-insensitive). POST and PUT both enforce this before committing to the database.
**Validates: Requirements 5.1, 5.2, 5.3, 5.4**

Property 2: Non-negative stock — `Stock` is always ≥ 0 at the database level (EF annotation + SQLite default). A request that sends a negative `Stock` is rejected with `400` before any write occurs.
**Validates: Requirements 3.1, 3.8**

Property 3: Filter identity — `GET api/products` with no query parameters returns the same set as the unfiltered list. Adding `search` and `type` can only reduce the result set, never add records that do not exist.
**Validates: Requirements 4.2, 4.4, 4.5, 4.9**

Property 4: Badge determinism — given a `Stock` value, exactly one badge state is shown: "Out of stock" (`= 0`), "Low stock" (`1–9`), or quantity (`≥ 10`). These ranges are mutually exclusive and exhaustive over non-negative integers.
**Validates: Requirements 3.6, 3.7**

Property 5: Client-side vs server-side parity — the `[Required]` and `[Range]` annotations on `ProductViewModel` match those on `Product`. A request that passes client-side validation will also pass server-side validation under normal conditions.
**Validates: Requirements 1.1, 2.1, 2.6**
