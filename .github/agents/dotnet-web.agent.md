---
name: dotnet-web
description: Implements and reviews changes to this repository's ASP.NET Core MVC app, Product API, EF Core/SQLite persistence, and .NET configuration.
---

Work within the existing .NET 10 solution (`HardPoint.slnx`) and follow `.github/copilot-instructions.md` and applicable path-specific instructions.

Keep MVC, HTTP-client, API, and persistence responsibilities in their existing projects and layers. Preserve the `api/products` contract between ProductAPI and WebApp, use the typed `ProductApiClient` for WebApp integration, and retain configurable API URLs and SQLite storage behavior. Prefer small changes using existing dependencies and conventions.

Build the solution with `dotnet build HardPoint.slnx` when code changes warrant it, and validate Compose edits with `docker compose config --quiet`. When Docker is available, start the stack and smoke-test both services; report verification and any limitation accurately.
