# Repository instructions

## Stack and layout

- This is a .NET 10 solution (`HardPoint.slnx`) pinned with `global.json`, with nullable reference types and implicit usings enabled.
- Central Package Management (CPM) is enabled in `Directory.Packages.props`. Never specify `Version` attributes directly on `PackageReference` elements in `.csproj` files.
- `ProductAPI/` is an ASP.NET Core Web API backed by EF Core and SQLite (`Database/ProductList.db`), with migrations in `ProductAPI/Migrations/` and an OpenAPI document (`ProductAPI.json`) generated at build time.
- `WebApp/` is an ASP.NET Core MVC app that calls ProductAPI through the typed `ProductApiClient`. Its API base URL is configured by `ProductApi:BaseUrl` (environment variable: `ProductApi__BaseUrl`).
- `ProductAPI.Tests/` holds the xUnit unit tests for the API; each test creates its own in-memory SQLite database, so EF Core query translation is the real one.
- `WebApp.Tests/` holds the xUnit unit tests for the MVC layer: controller actions, `ProductApiClient` against a fake `HttpMessageHandler`, and view-model validation.
- Coverage settings and the 70% line-coverage gate live in each test project's `.csproj`.
- `docker-compose.yml` runs ProductAPI and WebApp. The API is reachable locally at `http://localhost:5044`, and WebApp at `http://localhost:5168`. Within Compose, WebApp connects to `http://productapi:8080/`.

## Architecture and layer boundaries

- Keep responsibilities strictly in their layers:
  - MVC controllers and Razor views in `WebApp/`.
  - HTTP client integration in `WebApp/Services/ProductApiClient.cs`.
  - API endpoints and controllers in `ProductAPI/Controllers/`.
  - Persistence, EF Core entities and migrations in `ProductAPI/Models/` and `ProductAPI/Migrations/`.
- `WebApp` has no compile-time reference to `ProductAPI` and must never access the database directly.
- Preserve the API routes and response semantics consumed by `ProductApiClient`: camelCase JSON, enum values as numbers, and the documented status codes (`201/409` on POST, `204/400/404/409` on PUT, `204/404` on DELETE, `404` on GET by id).
- `ProductType` is defined in both projects with a known spelling difference (`PERIPHARALS` in ProductAPI, `PERIPHERALS` in WebApp). Change both plus the seed data together.
- Follow `.editorconfig`: C# uses 4 spaces, file-scoped namespaces, braces on new lines, and LF line endings.

## Build and verification

- Build the solution with `dotnet build HardPoint.slnx`.
- Run the unit tests with `dotnet test HardPoint.slnx`; add `-p:CollectCoverage=true` (per test project) when you need the coverage report and its quality gate.
- CI runs three jobs in parallel: `unit-tests` (both suites), `coverage` (one gate per suite, fails below 70% line coverage, uploads `artifacts/coverage/`) and `build-and-smoke`.
- Unit tests must not require a running ProductAPI: use the fake `HttpMessageHandler` (WebApp.Tests) or an in-memory SQLite database (ProductAPI.Tests).
- Validate Compose configuration with `docker compose config --quiet`.
- Start the stack with `docker compose up --build -d`; smoke-test `http://localhost:5168/`, `http://localhost:5044/` and `http://localhost:5044/api/products/1`, then stop with `docker compose down` (use `down -v` only when a database reset is wanted).
- Run the apps outside Docker with `dotnet run --project ProductAPI` and `dotnet run --project WebApp`.
- Keep changes focused; do not reformat unrelated files or commit SQLite `.db` files or build output.
