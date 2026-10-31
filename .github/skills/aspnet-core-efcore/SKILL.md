---
name: aspnet-core-efcore
description: Use when implementing or changing the ProductAPI, WebApp, their HTTP contract, EF Core/SQLite persistence, or ASP.NET Core configuration in this repository.
---

# ASP.NET Core and EF Core in this repository

- Start from the existing project boundaries: `ProductAPI/` owns the `api/products` endpoints and `ProductContext`; `WebApp/` owns MVC routes/views and `ProductApiClient`.
- Inspect both the API controller and typed client before changing a route, payload, status code, or error behavior. Keep them compatible or update both in the same change.
- Keep the SQLite database path and the API base URL configurable through the established application configuration; do not hard-code developer-specific paths or addresses.
- Match existing async patterns and pass cancellation tokens to HTTP/EF operations when the call chain supports them.
- Schema changes go through `ProductAPI/Migrations/`, which `Program.cs` applies with `Database.Migrate()` at startup. Seed data ships in the migrations, so changing it requires a new migration and a database reset (`docker compose down -v`) to reseed.
- `ProductAPI/ProductAPI.json` is regenerated during build by `Microsoft.Extensions.ApiDescription.Server`; never hand-edit it.
- Build with `dotnet build HardPoint.slnx`. If the change affects service integration, validate the Compose configuration and run the services when Docker is available.
