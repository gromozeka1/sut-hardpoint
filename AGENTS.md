# Repository Guidelines & Agent Instructions

## Solution Architecture & Module Organization

This repository contains a .NET 10 solution (`HardPoint.slnx`) pinned by `global.json` (`10.0.100`), consisting of four projects:

| Project | Type | Description |
|---|---|---|
| `WebApp/` | ASP.NET Core MVC | Product storefront at `http://localhost:5168` (Docker: `webapp:8080`). Product list with search/filter, create, edit, details and delete views. All data access goes through the typed `ProductApiClient`. |
| `ProductAPI/` | ASP.NET Core Web API | REST service at `http://localhost:5044` (Docker: `productapi:8080`) with SQLite persistence (`Database/ProductList.db`) via EF Core, migrations, seed data and OpenAPI generation. |
| `ProductAPI.Tests/` | xUnit | Unit tests for the API: controller behaviour, query filters, conflict handling and the wire contract of `ProductType`. Each test runs against its own in-memory SQLite database, so real EF Core query translation is exercised. |
| `WebApp.Tests/` | xUnit | Unit tests for the MVC layer: `ProductsController` actions (results, ModelState, TempData, 503 degradation), the `ProductApiClient` HTTP contract via a fake `HttpMessageHandler`, `ProductViewModel` validation and `HomeController`. |

### Dependency Flow & Isolation
- `WebApp` calls `ProductAPI` **over HTTP only**, through the typed `ProductApiClient`; the base URL comes from `ProductApi:BaseUrl` (env: `ProductApi__BaseUrl`).
- There is **no compile-time reference** between the two projects, and `WebApp` never touches the database.
- `ProductAPI` owns the `api/products` contract, the EF Core model, migrations and seed data.
- Local orchestration is defined in `docker-compose.yml`.

---

## Build & Development Commands

Always run commands from the repository root using the .NET 10 SDK:

```powershell# Restore and build the whole solution

dotnet build HardPoint.slnx

# Run all unit tests

dotnet test HardPoint.slnx

# Run one suite

dotnet test ProductAPI.Tests/ProductAPI.Tests.csproj
dotnet test WebApp.Tests/WebApp.Tests.csproj

# Run the unit tests with the coverage gate (fails below 70% line coverage)

dotnet test ProductAPI.Tests/ProductAPI.Tests.csproj -p:CollectCoverage=true
dotnet test WebApp.Tests/WebApp.Tests.csproj -p:CollectCoverage=true

# Start the stack (WebApp :5168, ProductAPI :5044)

docker compose up --build -d

# Stop the stack (preserves the SQLite volume 'productapi-data')
docker compose down

# Stop and reset the database (seed data is recreated on next start)
docker compose down -v

# Validate the Compose file after editing it
docker compose config --quiet

# Run without Docker (two separate terminals)
dotnet run --project ProductAPI
dotnet run --project WebApp
```

---

## Package & Dependency Management

- **Central Package Management (CPM)** is enforced via `Directory.Packages.props` (`CentralPackageTransitivePinningEnabled = true`); it lists only the packages this solution uses.
- **Never add a `Version` attribute** to a `PackageReference` in individual `.csproj` files.
- To add or upgrade a NuGet package:
  1. Add or update the version in `Directory.Packages.props` under `<PackageVersion Include="..." Version="..." />`.
  2. Reference it in the target `.csproj` with `<PackageReference Include="..." />`.

---

## Coding Style & Standards

Follow `.editorconfig` across all files:
- **C#**: 4 spaces indentation, file-scoped namespaces (`namespace Foo.Bar;`), LF line endings, nullable reference types enabled, implicit usings enabled, braces on new lines.
- **Naming**:
  - `PascalCase` for types, public members, and methods.
  - `_camelCase` for private fields.
  - `I`-prefix for interfaces.
- **Markdown / Config**: 2 spaces indentation, LF line endings, UTF-8.

---

## Application Architecture Guidelines

### 1. Layer responsibilities
- MVC controllers and Razor views live in `WebApp/Controllers` and `WebApp/Views`.
- `WebApp/Services/ProductApiClient.cs` is the **only** code that talks to ProductAPI. Pass cancellation tokens through the whole call chain, and keep the base URL configurable — never hard-code a host.
- API endpoints live in `ProductAPI/Controllers/ProductsController.cs`.
- Persistence lives in `ProductAPI/Models` (`ProductContext`) with migrations in `ProductAPI/Migrations`.

### 2. API contract
Keep response semantics stable; `ProductApiClient` depends on them.

| Method | Path | Success | Other outcomes |
|---|---|---|---|
| GET | `/` | `200 "Hello world!"` (health probe) | — |
| GET | `api/products?search=ryzen&type=CPU` | `200 [Product]` | `400` unknown `type` |
| GET | `api/products/{id}` | `200 Product` | `404` |
| POST | `api/products` | `201 Created` | `409` duplicate name |
| PUT | `api/products/{id}` | `204 No Content` | `400` id mismatch, `404`, `409` duplicate name |
| DELETE | `api/products/{id}` | `204 No Content` | `404` |

JSON is serialized with the default camelCase policy; enum values are serialized as numbers.

### 3. Configuration & data
- SQLite path: `ConnectionStrings:ProductContext` → `Data Source=Database/ProductList.db`; in Docker the directory is backed by the `productapi-data` volume.
- `ProductAPI/Program.cs` calls `Database.EnsureCreated()` at startup: schema **and** seed rows are created only when the database file does not exist yet. To reseed, delete the file or run `docker compose down -v`.
- Never commit SQLite `.db` files (ignored by `.gitignore`).
- `ProductAPI/ProductAPI.json` is regenerated at build time by `Microsoft.Extensions.ApiDescription.Server` — do not hand-edit it.

### 4. ProductType enum consistency
`ProductType` is defined independently in two places:

| Location | Values |
|---|---|
| `ProductAPI/Models/ProductType.cs` | `CPU`, `MONITOR`, `PERIPHARALS`, `EXTERNAL` |
| `WebApp/Models/ProductViewModel.cs` | `CPU`, `MONITOR`, `PERIPHERALS`, `EXTERNAL` |

Note the spelling difference (`PERIPHARALS` vs `PERIPHERALS`). Any change to values must be applied to both definitions and to the existing seed data together, because the serialized JSON contract changes with it.

### 5. Verification
- `dotnet build HardPoint.slnx` must finish with 0 warnings and 0 errors.
- Run `dotnet test HardPoint.slnx`; all tests must pass.
- Coverage settings live in each test `.csproj` (`CollectCoverage` property group): test assemblies, generated OpenAPI code, migrations and Razor view code are excluded, and a 70% line-coverage threshold fails the run (ProductAPI ≈80%, WebApp ≈82%).
- For Compose changes run `docker compose config --quiet`.
- When Docker is available, prove the change end-to-end the same way CI does:

```sh
docker compose up -d --build --wait --wait-timeout 180
curl --fail --silent http://localhost:5168/ | grep -q "Products"
curl --fail --silent http://localhost:5044/ | grep -q "Hello world"
curl --fail --silent http://localhost:5044/api/products/1
docker compose down
```

- Report exactly which checks ran; never present a config validation as an integration test.

---

## Commit & PR Guidelines
- Use imperative, concise commit messages (e.g., "Add stock filter to product list").
- Do not commit machine-specific files, binaries (`bin/`, `obj/`), SQLite `.db` runtime files, screenshots, or report outputs.
