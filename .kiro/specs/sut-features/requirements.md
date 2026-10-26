# Requirements Document

## Introduction

This document describes the requirements for five incremental enhancements to the ProductAPI
and WebApp services in this test automation SUT. The changes add server-side data validation,
client-side form validation, a stock-quantity field with visual badges, product search and
filter, and robust duplicate-name prevention. Authentication and authorization are explicitly
out of scope.

## Requirements

### 1. API-Level Data Validation

**As a** consumer of ProductAPI,
**I want** the API to reject invalid product data at the HTTP boundary,
**So that** bad data never reaches the database and callers receive actionable error responses.

#### Acceptance Criteria

**1.1** — `ProductAPI/Models/Product.cs` has the following constraints applied:
- `Name` is required (`[Required]`), non-nullable (`string`), and limited to 100 characters (`[StringLength(100)]`).
- `Description` remains optional (`string?`) and is limited to 1 000 characters (`[StringLength(1000)]`).
- `Price` must be zero or greater (`[Range(0, int.MaxValue)]`).

**1.2** — `POST api/products` with a missing or empty `Name` returns `400 Bad Request` with a validation problem details body.

**1.3** — `POST api/products` with a `Name` longer than 100 characters returns `400 Bad Request`.

**1.4** — `POST api/products` with a negative `Price` returns `400 Bad Request`.

**1.5** — `PUT api/products/{id}` enforces the same validation rules as POST and returns `400 Bad Request` on violation.

**1.6** — Valid requests continue to return `201 Created` (POST) and `204 No Content` (PUT) as before.

---

### 2. Client-Side Form Validation

**As a** WebApp user,
**I want** form validation errors to appear immediately in the browser without a round-trip,
**So that** I can correct mistakes without waiting for a server response.

#### Acceptance Criteria

**2.1** — The `_Layout.cshtml` shared layout renders `_ValidationScriptsPartial` so that `jquery.validate` and `jquery.validate.unobtrusive` are loaded on every page that uses the layout.

**2.2** — On the Create Product form, submitting with an empty `Name` field shows an inline validation message (e.g., "The Name field is required.") without a page reload.

**2.3** — On the Create Product form, entering a `Name` longer than 100 characters shows an inline validation message without a page reload.

**2.4** — On the Create Product form, entering a negative `Price` shows an inline validation message without a page reload.

**2.5** — The same client-side validation applies on the Edit Product form.

**2.6** — When client-side validation passes, the form submits normally and server-side validation is still enforced as a second layer.

---

### 3. Stock Quantity Field

**As a** product manager,
**I want** each product to carry a stock quantity,
**So that** the product list and detail page can flag items that are low or out of stock.

#### Acceptance Criteria

**3.1** — `ProductAPI/Models/Product.cs` gains a `Stock` property (`int`) annotated with `[Range(0, int.MaxValue)]`. The field defaults to `0`.

**3.2** — `WebApp/Models/ProductViewModel.cs` gains a matching `Stock` property (`int`) with `[Range(0, int.MaxValue)]`.

**3.3** — An EF Core migration adds the `Stock` column to the SQLite database with a default value of `0` so that existing rows are not broken.

**3.4** — The seed data in `ProductContext.OnModelCreating` is updated to supply explicit `Stock` values for all three seed products: Id 1 → 25, Id 2 → 5, Id 3 → 0.

**3.5** — The Create and Edit forms in WebApp include a `Stock` input field with label "Stock".

**3.6** — The product list (`Index` view) displays a stock badge in the row for each product:
- `Stock == 0` → red badge reading "Out of stock".
- `0 < Stock < 10` → yellow/warning badge reading "Low stock".
- `Stock >= 10` → green badge showing the quantity (e.g., "25 in stock").

**3.7** — The product details view (`Details` view) shows the `Stock` value and applies the same badge logic as the list.

**3.8** — `POST api/products` and `PUT api/products/{id}` accept and persist the `Stock` field. A missing `Stock` in the request body defaults to `0`.

**3.9** — `GET api/products` and `GET api/products/{id}` include the `Stock` field in the JSON response.

---

### 4. Product Search and Filter

**As a** WebApp user,
**I want** to search products by name and filter by type on the product list page,
**So that** I can quickly find products in a large catalogue.

#### Acceptance Criteria

**4.1** — `GET api/products` accepts two optional query parameters: `search` (string, case-insensitive substring match on `Name`) and `type` (string, exact match on `ProductType` enum name).

**4.2** — When `search` is provided, the response includes only products whose `Name` contains the search string (case-insensitive). An empty or absent `search` returns all products.

**4.3** — When `type` is provided, the response includes only products of that `ProductType`. An invalid `type` value returns `400 Bad Request`.

**4.4** — `search` and `type` can be combined; both filters are applied with AND logic.

**4.5** — When no products match the filters, the API returns `200 OK` with an empty JSON array (not `404`).

**4.6** — The WebApp product list page renders a search form above the table containing: a text input (label/placeholder "Search by name"), a dropdown ("Filter by type") populated from the `ProductType` enum with an "All types" option, and a "Search" submit button.

**4.7** — When the user submits the search form, the WebApp passes `search` and `type` as query parameters on its call to `ProductApiClient.GetAllAsync`.

**4.8** — The current filter values are preserved in the search form after submission.

**4.9** — Navigating to the product list without query parameters shows all products (unchanged default behaviour).

---

### 5. Duplicate Name Prevention

**As a** product manager,
**I want** the system to prevent two products from sharing the same name,
**So that** the catalogue stays unambiguous and the UI search works reliably.

#### Acceptance Criteria

**5.1** — `POST api/products` with a `Name` that matches an existing product's name (case-insensitive) returns `409 Conflict`. The response body is a ProblemDetails object with `title: "Conflict"` and a `detail` field, e.g., `"A product with the name 'Headphones' already exists."`.

**5.2** — The existing duplicate-ID check in `PostProduct` (which returns an opaque 500) is replaced by the name-uniqueness check. Callers no longer receive `500` for duplicate submissions.

**5.3** — `PUT api/products/{id}` with a `Name` that matches another product's name (case-insensitive, excluding the product being updated) returns `409 Conflict` with the same ProblemDetails shape.

**5.4** — `PUT api/products/{id}` updating a product's other fields while keeping the same `Name` succeeds with `204 No Content`.

**5.5** — The WebApp `Create` action handles a `409` response from ProductAPI by adding a `ModelState` error: `"A product with this name already exists."`, and re-renders the Create form.

**5.6** — The WebApp `Edit` action handles a `409` response from ProductAPI by adding a `ModelState` error and re-rendering the Edit form.

## Glossary

- **ProductType** — an enum defined in three places: `ProductAPI/Models/ProductType.cs`, `Tests/Models/Product.cs`, and `WebApp/Models/ProductViewModel.cs`. The API and Tests use the spelling `PERIPHARALS`; WebApp uses `PERIPHERALS`. This inconsistency is pre-existing and is not corrected by this spec.
- **Stock** — the integer quantity of units available for a product. Zero means out of stock.
- **409 Conflict** — the HTTP status code returned when a business-rule uniqueness constraint is violated (duplicate product name).
- **ProblemDetails** — the RFC 7807 JSON error response shape used by ASP.NET Core for structured API error responses.
- **`_ValidationScriptsPartial`** — a Razor partial view in `WebApp/Views/Shared/` that renders the jQuery Validation scripts needed for client-side form validation.
