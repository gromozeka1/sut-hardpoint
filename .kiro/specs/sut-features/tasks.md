# Implementation Plan: SUT Feature Enhancements

## Overview

Tasks are numbered sequentially and grouped by layer. The API layer (tasks 1–9) is implemented
first because WebApp (10–18) and Tests (19–38) both depend on its contracts. Within each wave,
tasks that touch different files can be worked in parallel. Explicit dependencies are listed
per task.

## Tasks

### ProductAPI — Model and Database

- [x] 1. Add data annotations to `ProductAPI/Models/Product.cs`
  - Make `Name` non-nullable (`string`), add `[Required]`, `[StringLength(100)]`.
  - Add `[StringLength(1000)]` to `Description` (keep `string?`).
  - Add `[Range(0, int.MaxValue)]` to `Price`.
  - Acceptance criteria: 1.1

- [x] 2. Add `Stock` field to `ProductAPI/Models/Product.cs`
  - Add `public int Stock { get; set; }` annotated with `[Range(0, int.MaxValue)]`.
  - Apply together with task 1 (same file).
  - Acceptance criteria: 3.1

- [x] 3. Initialise EF Core migrations for ProductAPI
  - Run `dotnet ef migrations add InitialCreate --project ProductAPI` to baseline the existing schema.
  - Verify the generated migration matches the current table shape (Id, Name, Description, ProductType, Price).
  - Ensure `Microsoft.EntityFrameworkCore.Design` is referenced in `Directory.Packages.props`.

- [x] 4. Add `AddStockToProduct` EF migration
  - Run `dotnet ef migrations add AddStockToProduct --project ProductAPI`.
  - Confirm the generated `Up` method adds `Stock INTEGER NOT NULL DEFAULT 0`.
  - Depends on tasks 2 and 3.

- [x] 5. Switch startup from `EnsureCreated` to `Migrate` in `ProductAPI/Program.cs`
  - Replace `context.Database.EnsureCreated()` with `context.Database.Migrate()`.
  - Note: run `docker compose down -v` before first run to destroy the existing SQLite volume.
  - Depends on task 3.

- [x] 6. Update seed data in `ProductAPI/Models/ProductContext.cs`
  - Add `Stock` values to all three seed products: Id 1 (Ryzen 7 7800X3D) → 25, Id 2 (Dell UltraSharp 27) → 5, Id 3 (Logitech MX Master 3S) → 0.
  - Acceptance criteria: 3.4
  - Depends on task 2.

### ProductAPI — Controller Logic

- [x] 7. Fix `POST api/products` duplicate check — return 409 instead of 500
  - Remove the `ProductExists(product.Id)` call.
  - Add a case-insensitive name-uniqueness `AnyAsync` check.
  - Return `Conflict(new ProblemDetails { Title = "Conflict", Detail = $"A product with the name '{product.Name}' already exists.", Status = 409 })` on name collision.
  - Acceptance criteria: 5.1, 5.2
  - Depends on task 1 (Name is now non-nullable).

- [x] 8. Add duplicate name check to `PUT api/products/{id}`
  - Before saving, run `AnyAsync` for another product with the same name excluding the current id.
  - Return the same `409 Conflict` ProblemDetails shape as task 7 on collision.
  - A PUT that keeps the product's own name must still return `204 No Content`.
  - Acceptance criteria: 5.3, 5.4
  - Depends on task 7 (consistent ProblemDetails shape).

- [x] 9. Add search/filter query parameters to `GET api/products`
  - Add `[FromQuery] string? search` and `[FromQuery] string? type` parameters to `GetProduct()`.
  - Build `IQueryable<Product>` and apply `Where` clauses: case-insensitive contains for `search`; enum-parse for `type` (return `BadRequest()` on parse failure).
  - Both filters are ANDed; absent parameters return all products; empty result returns `200 OK` with `[]`.
  - Acceptance criteria: 4.1–4.5

### WebApp — Model and Service

- [x] 10. Add `Stock` field to `WebApp/Models/ProductViewModel.cs`
  - Add `[Range(0, int.MaxValue)] public int Stock { get; set; }`.
  - Acceptance criteria: 3.2

- [x] 11. Add client-side validation scripts to `WebApp/Views/Shared/_Layout.cshtml`
  - Add `@await Html.PartialAsync("_ValidationScriptsPartial")` after the Bootstrap bundle script tag, before `@await RenderSectionAsync("Scripts", required: false)`.
  - Acceptance criteria: 2.1

- [x] 12. Update `ProductApiClient.GetAllAsync` to accept and forward search/filter params
  - Add `string? search = null` and `string? type = null` optional parameters.
  - Build the query string conditionally (URL-encode values) and append to the endpoint URL.
  - Acceptance criteria: 4.7

- [x] 13. Handle `409 Conflict` in `ProductApiClient.CreateAsync` and `UpdateAsync`
  - Change the return type of both methods to surface a structured result, e.g., `(bool success, string? conflictMessage)`.
  - On `409`, read `ProblemDetails.Detail` from the response body.
  - On other non-success statuses, throw `HttpRequestException` as before.
  - Acceptance criteria: 5.5, 5.6

### WebApp — Controller

- [x] 14. Pass search/filter parameters through `ProductsController.Index`
  - Add `string? search` and `string? type` action parameters.
  - Set `ViewBag.Search = search` and `ViewBag.Type = type` for form repopulation.
  - Pass them to `ProductApiClient.GetAllAsync`.
  - Acceptance criteria: 4.7, 4.8
  - Depends on task 12.

- [x] 15. Surface `409 Conflict` errors in `ProductsController.Create` and `Edit` POST actions
  - Consume the structured result from `CreateAsync`/`UpdateAsync`.
  - On conflict, call `ModelState.AddModelError(string.Empty, conflictMessage)` and re-render the form.
  - Acceptance criteria: 5.5, 5.6
  - Depends on task 13.

### WebApp — Views

- [x] 16. Add `Stock` input to `WebApp/Views/Products/_ProductForm.cshtml`
  - Add a `<div class="mb-3">` block with label (`asp-for="Stock"`), `type="number"` input (`min="0"`), and validation span.
  - Acceptance criteria: 3.5
  - Depends on task 10.

- [x] 17. Create `WebApp/Views/Shared/_StockBadge.cshtml` partial view (new file)
  - Model is `int`.
  - `== 0` → `<span class="badge bg-danger">Out of stock</span>`
  - `< 10` → `<span class="badge bg-warning text-dark">Low stock</span>`
  - `>= 10` → `<span class="badge bg-success">{n} in stock</span>`
  - Acceptance criteria: 3.6, 3.7

- [x] 18. Add Stock column + badge to `Index.cshtml` and Details to `Details.cshtml`
  - Index: add "Stock" `<th>` and render `@await Html.PartialAsync("_StockBadge", product.Stock)` in each row.
  - Details: add Stock `<dt>/<dd>` pair using the `_StockBadge` partial.
  - Add search/filter form above the product table in Index: text input (`name="search"`), type dropdown (`name="type"`, includes "All types" blank option, pre-selected from `ViewBag.Type`), "Search" submit button.
  - Acceptance criteria: 3.6, 3.7, 4.6, 4.8
  - Depends on tasks 10, 14, 17.

### Tests — Models and Support

- [x] 19. Add `Stock` to `Tests/Models/Product.cs`
  - Add `public int Stock { get; set; }`.

- [x] 20. Update `ProductsApi.CreateAsync` to include `Stock` in the request body
  - Add `product.Stock` to the anonymous object serialised as JSON.
  - Depends on task 19.

- [x] 21. Add `GetAllAsync` overload to `Tests/Support/ProductsApi.cs`
  - Signature: `Task<IAPIResponse> GetAllAsync(string? search = null, string? type = null)`.
  - Build query string conditionally; call `GET products?...`.

### Tests — API Scenarios

New scenarios added to `Tests/Features/ProductApi.feature`; step bindings added to `Tests/Steps/ProductApiSteps.cs`.

- [x] 22. API scenario: POST with missing Name returns 400
  - Send POST with Name omitted or empty; assert `response status is 400`.
  - Acceptance criteria: 1.2

- [x] 23. API scenario: POST with Name longer than 100 characters returns 400
  - Build a 101-character name; assert `response status is 400`.
  - Acceptance criteria: 1.3

- [x] 24. API scenario: POST with negative Price returns 400
  - Send POST with `Price = -1`; assert `response status is 400`.
  - Acceptance criteria: 1.4

- [x] 25. API scenario: POST duplicate name returns 409
  - Background creates a product; POST the same name again; assert `response status is 409` and response detail contains the product name.
  - New step: `Then the response detail contains {string}`.
  - Acceptance criteria: 5.1
  - Depends on task 7.

- [x] 26. API scenario: PUT duplicate name returns 409
  - Background creates two products; rename the second to the first's name; assert `response status is 409`.
  - Acceptance criteria: 5.3
  - Depends on task 8.

- [x] 27. API scenario: PUT same name on same product returns 204
  - Background creates a product; PUT with the same Name; assert `response status is 204`.
  - Acceptance criteria: 5.4
  - Depends on task 8.

- [x] 28. API scenario: GET with search filter returns only matching products
  - Create a product with a distinctive name; search for a substring; assert the product is in the response and non-matching products are absent.
  - New steps: `When I search for products with name {string}`, `Then the product list contains {string}`.
  - Acceptance criteria: 4.2
  - Depends on tasks 9 and 21.

- [x] 29. API scenario: GET with type filter returns only products of that type
  - Filter by a specific type; assert only products of that type are returned.
  - New step: `When I filter products by type {string}`.
  - Acceptance criteria: 4.3
  - Depends on tasks 9 and 21.

- [x] 30. API scenario: GET with invalid type returns 400
  - Send `GET api/products?type=INVALID`; assert `response status is 400`.
  - Acceptance criteria: 4.3
  - Depends on tasks 9 and 21.

- [x] 31. API scenario: Stock field is persisted and returned
  - POST a product with `Stock = 7`; GET it back; assert `Stock` equals `7`.
  - New step: `Then the product has stock {int}`.
  - Acceptance criteria: 3.8, 3.9
  - Depends on tasks 2, 19, 20.

### Tests — UI Scenarios

New scenarios added to `Tests/Features/Product.feature`; step bindings added to `Tests/Steps/ProductSteps.cs`; page objects updated in `Tests/Pages/`.

- [x] 32. Update `Tests/Pages/ProductPage.cs` — fill the Stock field
  - Add `TxtStock` locator (`Page.Locator("#Stock")`).
  - Include `TxtStock.FillAsync(product.Stock.ToString())` in `CreateProduct`.
  - Depends on tasks 16 and 19.

- [x] 33. Update `Tests/Pages/ProductListPage.cs` — search form and badge interactions
  - Add `TxtSearch` (`Page.GetByPlaceholder("Search by name")`), `SelectTypeFilter` (combobox by role/name), `BtnSearch` locators.
  - Add `SearchAsync(string? searchTerm, string? type)` method.
  - Add `StockBadgeForProduct(string productName)` locator.
  - Depends on tasks 17 and 18.

- [x] 34. UI scenario: Create product with Stock ≥ 10 shows quantity badge
  - Background ensures product cleaned up. Create with `Stock = 15`; assert list shows green badge "15 in stock".
  - New step: `Then I see the stock badge {string} for product {string}`.
  - Acceptance criteria: 3.6
  - Depends on tasks 32 and 33.

- [x] 35. UI scenario: Create product with Stock = 0 shows "Out of stock" badge
  - Create with `Stock = 0`; assert "Out of stock" badge visible in the list row.
  - Acceptance criteria: 3.6
  - Depends on tasks 32 and 33.

- [x] 36. UI scenario: Create product with Stock < 10 shows "Low stock" badge
  - Create with `Stock = 3`; assert "Low stock" badge visible in the list row.
  - Acceptance criteria: 3.6
  - Depends on tasks 32 and 33.

- [x] 37. UI scenario: Submit Create form with empty Name triggers client-side validation
  - Navigate to Create page; submit without filling Name.
  - Assert inline validation message is visible without URL change.
  - New step: `Then I see a validation message {string}`.
  - Acceptance criteria: 2.2
  - Depends on task 11.

- [x] 38. UI scenario: Create product with duplicate name shows server-side error
  - Background ensures a product exists via API; attempt to create same name via UI.
  - Assert form re-renders with error "A product with this name already exists."
  - Acceptance criteria: 5.5
  - Depends on tasks 7 and 15.

- [x] 39. UI scenario: Search by name filters the product list
  - Background ensures test products exist. Submit search form with a term matching only one product.
  - Assert matched product row is visible; a non-matching product row is absent.
  - New steps: `When I search for products by name {string}`, `Then I see product {string} in the list`, `Then I do not see product {string} in the list`.
  - Acceptance criteria: 4.6–4.8
  - Depends on tasks 9, 14, 18, 33.

- [x] 40. UI scenario: Filter by type filters the product list
  - Select a type from the filter dropdown; assert only products of that type appear.
  - Acceptance criteria: 4.6–4.8
  - Depends on tasks 9, 14, 18, 33.

- [x] 41. Update existing `Product.feature` DataTable to include Stock
  - Add `| Stock |` column with value `10` to the "Create product and verify the details" DataTable.
  - Update the "I see all the product details are created as expected" step to assert Stock is visible on the Details page if needed.
  - Depends on task 32.

## Task Dependency Graph

```json
{
  "waves": [
    {
      "wave": 1,
      "tasks": ["1", "2", "3"]
    },
    {
      "wave": 2,
      "tasks": ["4", "5", "6", "7", "9"]
    },
    {
      "wave": 3,
      "tasks": ["8", "10", "11", "12", "13", "19", "21"]
    },
    {
      "wave": 4,
      "tasks": ["14", "15", "16", "17", "20"]
    },
    {
      "wave": 5,
      "tasks": ["18", "22", "23", "24", "31"]
    },
    {
      "wave": 6,
      "tasks": ["25", "26", "27", "28", "29", "30", "32", "33"]
    },
    {
      "wave": 7,
      "tasks": ["34", "35", "36", "37", "38", "39", "40", "41"]
    }
  ]
}
```

## Notes

- The `PERIPHARALS` / `PERIPHERALS` enum spelling inconsistency between API and WebApp is pre-existing and is not corrected by this plan. Any correction must be coordinated across `ProductAPI/Models/ProductType.cs`, `Tests/Models/Product.cs`, `WebApp/Models/ProductViewModel.cs`, and existing database data.
- Before running the services for the first time after task 5, run `docker compose down -v` to destroy the existing SQLite volume so the migration runs on a fresh database.
- New `PackageReference` items (e.g., `Microsoft.EntityFrameworkCore.Design`) must be added to `Directory.Packages.props` without version attributes on individual `<PackageReference>` elements, following the project's central package management convention.
- All new test scenarios require Docker Compose services to be running (`docker compose up --build -d`) before `dotnet test`.
