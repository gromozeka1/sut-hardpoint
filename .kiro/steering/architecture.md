# Solution Architecture

## Projects

| Project | Role |
|---|---|
| `WebApp/` | ASP.NET Core MVC storefront — product list with search/filter, create, edit, details, delete. Talks to the API only through `ProductApiClient`. |
| `ProductAPI/` | ASP.NET Core Web API — `api/products` endpoints, EF Core + SQLite persistence, migrations, seed data, OpenAPI output. |

Solution file: `HardPoint.slnx` (.NET 10, `global.json` pins SDK to `10.0.100`).

## Dependency Flow

```
WebApp ──HTTP (runtime)──▶ ProductAPI   (via typed ProductApiClient, base URL from ProductApi:BaseUrl)
ProductAPI ──EF Core──▶ SQLite           (Database/ProductList.db, volume productapi-data)
```

There is no compile-time reference between the two projects: `WebApp` never references `ProductAPI` assemblies and never opens the database. The API is the single source of truth for data; the UI is a projection of it.

## Key Design Patterns

### Typed HttpClient boundary
`WebApp/Services/ProductApiClient.cs` is the only code that performs HTTP calls. It is registered in `WebApp/Program.cs`, receives its base URL from configuration (`ProductApi:BaseUrl` / `ProductApi__BaseUrl`) and forwards cancellation tokens. Every route, payload or status-code change must be made on both the controller and this client in the same change.

### Query parameters instead of endpoints
`GET api/products` accepts optional `search` (case-insensitive `Name` substring) and `type` (parsed to `ProductType`, `400` when unknown) query parameters, so filtering stays in the API rather than in the UI.

### Migrations + seed data
`ProductAPI/Program.cs` calls `Database.Migrate()` at startup, so pending migrations from `ProductAPI/Migrations/` are applied before the app serves requests. The three seed rows defined in `ProductContext.OnModelCreating` ship in the initial migration and are inserted once, when the migrations first run. Re-seeding therefore requires deleting `Database/ProductList.db` or `docker compose down -v`; restarting alone does not re-insert them.

### ProblemDetails-style conflicts
Duplicate-name handling on POST/PUT returns `409` with a `ProblemDetails` body (`Title`, `Detail`, `Status`) instead of a generic 500, which is what the UI surfaces to users.

### Generated OpenAPI document
`Microsoft.Extensions.ApiDescription.Server` writes `ProductAPI/ProductAPI.json` on every build (`OpenApiDocumentsDirectory = .`). It is a build artifact that is committed; regenerate rather than edit.

### Container isolation
`docker-compose.yml` wires the two services: `webapp` gets `ProductApi__BaseUrl=http://productapi:8080/` and depends on `productapi`; SQLite is persisted in the `productapi-data` volume mounted at `/app/Database`. Service names double as DNS hostnames on the Compose network — never point the WebApp at `localhost` inside the stack.

## NuGet Package Management
All package versions are centralised in `Directory.Packages.props`. `.csproj` files list `PackageReference` without version attributes. `CentralPackageTransitivePinningEnabled` is `true`. Do not add version attributes to individual `PackageReference` elements.
