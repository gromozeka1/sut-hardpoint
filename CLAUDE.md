# CLAUDE.md - Assistant Guidelines

## Solution Overview
This repository is a .NET 10 solution (`HardPoint.slnx`) containing an ASP.NET Core MVC web application, an ASP.NET Core REST API with EF Core + SQLite persistence, and their xUnit test projects — a PC-hardware product-management application.

### Projects
- `WebApp/`: ASP.NET Core MVC storefront at `localhost:5168`. Calls the API through the typed `ProductApiClient`.
- `ProductAPI/`: ASP.NET Core Web API at `localhost:5044` with SQLite persistence via EF Core, migrations and seed data.
- `ProductAPI.Tests/`: xUnit unit tests for the API, run against an isolated in-memory SQLite database per test.
- `WebApp.Tests/`: xUnit unit tests for the MVC layer, with `ProductApiClient` exercised through a fake `HttpMessageHandler` (no server involved).

There is no compile-time reference between the two application projects; `WebApp` reaches `ProductAPI` over HTTP only.

---

## Essential Commands

### Build
- `dotnet build HardPoint.slnx` - Restores and builds the whole solution.

### Unit tests
- `dotnet test HardPoint.slnx` - Runs both xUnit suites (`ProductAPI.Tests`, `WebApp.Tests`).
- `dotnet test ProductAPI.Tests/ProductAPI.Tests.csproj -p:CollectCoverage=true` - coverlet coverage plus the 70% line-coverage gate (reports in `artifacts/coverage/productapi/`).
- `dotnet test WebApp.Tests/WebApp.Tests.csproj -p:CollectCoverage=true` - same for the WebApp (reports in `artifacts/coverage/webapp/`).

### Run with Docker Compose
- `docker compose up --build -d` - Builds and launches `productapi` (5044) and `webapp` (5168).
- `docker compose down` - Stops services, preserving the SQLite data volume.
- `docker compose down -v` - Stops services and clears the data volume (re-seeds on next start).
- `docker compose config --quiet` - Validates the Compose file after edits.

### Run without Docker
- `dotnet run --project ProductAPI` - API at `http://localhost:5044`.
- `dotnet run --project WebApp` - UI at `http://localhost:5168`.

---

## Key Development Rules

### Central Package Management (CPM)
- Package versions are managed in `Directory.Packages.props`.
- **Never** add a `Version` attribute to a `<PackageReference>` in individual `.csproj` files.

### C# & Code Style (.editorconfig)
- Indentation: 4 spaces for C#, 2 spaces for JSON/Markdown/YAML.
- Line endings: LF across all files.
- Namespaces: file-scoped (`namespace Project.SubNamespace;`).
- Nullable reference types: enabled; preserve meaningful annotations.
- Braces on new lines; `_camelCase` private fields; `PascalCase` public members.

### Layer Boundaries
1. **WebApp** owns MVC routes, Razor views and `ProductApiClient`. It must never open a database connection.
2. **ProductAPI** owns the `api/products` endpoints, `ProductContext`, migrations and seed data.
3. Keep the HTTP contract stable: camelCase JSON, enum values serialized as numbers, status codes as documented in `AGENTS.md`.
4. Keep `ProductApi:BaseUrl` and the SQLite path configurable; never hard-code hosts or developer-specific paths.
5. Match existing async patterns and pass cancellation tokens through HTTP/EF call chains.

### Data & Configuration
- SQLite file: `Database/ProductList.db`; in Docker it lives in the `productapi-data` volume.
- `EnsureCreated()` seeds three products the first time the database file is created; reseed with `docker compose down -v`.
- `ProductAPI.json` (OpenAPI) is generated at build time — do not edit it manually.
- `ProductType` exists in both `ProductAPI/Models/ProductType.cs` (`PERIPHARALS`) and `WebApp/Models/ProductViewModel.cs` (`PERIPHERALS`); update both plus seed data whenever values change.

### Verification
- Build must be clean (0 warnings, 0 errors).
- Unit tests must pass: `dotnet test HardPoint.slnx`.
- When Docker is available, start the stack and smoke-test both endpoints as CI does; otherwise report that limitation instead of claiming verification.
