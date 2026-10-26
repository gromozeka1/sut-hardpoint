# HardPoint

HardPoint is a PC-hardware product-management application — an ASP.NET Core MVC storefront (**WebApp**) backed by an ASP.NET Core REST API (**ProductAPI**) with EF Core and SQLite.

## Repository structure

- `HardPoint.slnx` — solution containing both applications and their unit test projects.
- `WebApp/` — MVC UI for viewing, searching, creating, editing and deleting products. All data access goes through the typed `ProductApiClient` HTTP client; the WebApp has no database access of its own.
- `ProductAPI/` — REST API (`api/products`) with EF Core + SQLite persistence, migrations and OpenAPI generation.
- `ProductAPI.Tests/` — xUnit unit tests for the API (controller behaviour, query filters, validation and conflict handling) against an isolated in-memory SQLite database per test.
- `WebApp.Tests/` — xUnit unit tests for the MVC layer: `ProductsController` actions and ModelState/TempData handling, the `ProductApiClient` HTTP contract (via a fake `HttpMessageHandler`) and `ProductViewModel` validation.
- `docker-compose.yml` + the two service Dockerfiles — the containerized stack used locally and in CI.
- `Directory.Packages.props` — Central Package Management versions for this repository.
- `global.json` — pins the .NET SDK (`10.0.100`, `rollForward: latestFeature`).
- `nuget.config` — package sources (`nuget.org` only).
- `.github/workflows/dotnet.yml` — CI: build + containerized smoke test.

The solution targets .NET 10.

## Requirements

- .NET 10 SDK (see `global.json`)
- Docker Engine or Docker Desktop with Docker Compose v2
- Ports `5168` (WebApp) and `5044` (ProductAPI) free

## Build

```sh
dotnet build HardPoint.slnx
```

## Unit tests

```sh
dotnet test HardPoint.slnx                     # both suites
dotnet test ProductAPI.Tests/ProductAPI.Tests.csproj
dotnet test WebApp.Tests/WebApp.Tests.csproj
```

Coverage with the quality gate (coverlet; each run fails when total line coverage drops below 70% — configured in the test project's `.csproj`):

```sh
dotnet test ProductAPI.Tests/ProductAPI.Tests.csproj -p:CollectCoverage=true
dotnet test WebApp.Tests/WebApp.Tests.csproj -p:CollectCoverage=true
```

Reports are written to `artifacts/coverage/productapi/` and `artifacts/coverage/webapp/` (`coverage.cobertura.xml` and `coverage.json`). Excluded from the metric: the test assemblies, source-generated OpenAPI comment code, EF Core migrations, and Razor view code (views only execute through the HTTP pipeline, which the `build-and-smoke` stage covers).

## Run with Docker Compose

```sh
docker compose up --build -d
```

- WebApp: http://localhost:5168
- ProductAPI: http://localhost:5044

Inside the Compose network the WebApp reaches the API at `http://productapi:8080/` (set via `ProductApi__BaseUrl`); the host URLs above are for browsers and tests outside the containers.

Stop the services (keeps the `productapi-data` volume):

```sh
docker compose down
```

Reset the database (deletes the volume, so the seed data is recreated on next start):

```sh
docker compose down -v
```

## Run with pre-built images

Published images are on Docker Hub: [`gromozeka1/hardpoint-productapi`](https://hub.docker.com/r/gromozeka1/hardpoint-productapi) and [`gromozeka1/hardpoint-webapp`](https://hub.docker.com/r/gromozeka1/hardpoint-webapp). Tags: `latest` (every push to `main`), `sha-<shortsha>` (exact commit) and `vX.Y.Z` (releases).

```sh
docker pull gromozeka1/hardpoint-productapi:latest
docker pull gromozeka1/hardpoint-webapp:latest

# Shared network so the WebApp can reach the API by container name
docker network create hardpoint

docker run -d --name productapi --network hardpoint \
  -p 5044:8080 \
  -v productapi-data:/app/Database \
  gromozeka1/hardpoint-productapi:latest

docker run -d --name webapp --network hardpoint \
  -p 5168:8080 \
  -e ProductApi__BaseUrl=http://productapi:8080/ \
  gromozeka1/hardpoint-webapp:latest
```

- WebApp: http://localhost:5168
- ProductAPI: http://localhost:5044

The named volume keeps the SQLite database (with seed data) across restarts; without `-v` the data is lost when the container is removed. Both containers run as a non-root user and listen on container port `8080`.

Tear everything down:

```sh
docker rm -f webapp productapi
docker network rm hardpoint
```

## Run without Docker

Start each project in its own terminal:

```sh
dotnet run --project ProductAPI    # http://localhost:5044
dotnet run --project WebApp        # http://localhost:5168
```

Both `launchSettings.json` files provide `http`, `https` and `Container (Dockerfile)` profiles; the `http` profile is the one that matches the URLs above.

## Configuration

| Setting | Default | Purpose |
|---|---|---|
| `ProductApi:BaseUrl` (`ProductApi__BaseUrl`) | `http://localhost:5044/` | WebApp → ProductAPI base URL |
| `ConnectionStrings:ProductContext` | `Data Source=Database/ProductList.db` | SQLite database file |
| `ASPNETCORE_ENVIRONMENT` | `Development` in Compose | Enables development logging/OpenAPI |

`ProductAPI/Program.cs` calls `Database.EnsureCreated()` at startup: the schema and the seed rows below are created only when the database file does not exist yet. Delete the file (or `docker compose down -v`) to reseed.

## API reference

Base URL (local): `http://localhost:5044`

| Method | Path | Success | Other outcomes |
|---|---|---|---|
| GET | `/` | `200 "Hello world!"` (health probe) | — |
| GET | `api/products?search=ryzen&type=CPU` | `200 [Product]` | `400` for an unknown `type` |
| GET | `api/products/{id}` | `200 Product` | `404` |
| POST | `api/products` | `201 Created` | `409` if the name already exists |
| PUT | `api/products/{id}` | `204 No Content` | `400` id mismatch, `404`, `409` name conflict |
| DELETE | `api/products/{id}` | `204 No Content` | `404` |

`search` matches `Name` (case-insensitive substring), `type` matches `ProductType`.

### Seed data

| Id | Name | ProductType | Price | Stock |
|---|---|---|---|---|
| 1 | Ryzen 7 7800X3D | CPU | 399 | 25 |
| 2 | Dell UltraSharp 27 | MONITOR | 499 | 5 |
| 3 | Logitech MX Master 3S | PERIPHARALS | 99 | 0 |

### ProductType enum

`ProductType` is defined independently in three places — keep them aligned when adding values:

| Location | Values |
|---|---|
| `ProductAPI/Models/ProductType.cs` | `CPU`, `MONITOR`, `PERIPHARALS`, `EXTERNAL` |
| `WebApp/Models/ProductViewModel.cs` | `CPU`, `MONITOR`, `PERIPHERALS`, `EXTERNAL` |

Note the existing spelling difference: `PERIPHARALS` (API) vs `PERIPHERALS` (WebApp). Do not "fix" it in one place only — the serialized JSON values, the seed data and any consumer of the API must change together.

## CI

`.github/workflows/dotnet.yml` runs on pushes and pull requests to `main`, with three parallel jobs:

**`unit-tests`**
1. Restore and build `HardPoint.slnx` (Release).
2. `dotnet test HardPoint.slnx` — runs `ProductAPI.Tests` and `WebApp.Tests`.

**`coverage`**
1. Run each suite with `-p:CollectCoverage=true` so coverlet instruments the run.
2. Fail the job if total line coverage is below the 70% threshold (currently ≈80% for ProductAPI and ≈82% for WebApp).
3. Upload `artifacts/coverage/` as the `coverage-report` artifact (even when a gate fails).

**`build-and-smoke`**
1. Validate `docker-compose.yml`.
2. Restore and build `HardPoint.slnx` (Release).
3. `docker compose up -d --build --wait --wait-timeout 180`.
4. Smoke test: WebApp home page renders, API root answers, and the seeded product is returned by `GET api/products/1`.
5. `docker compose down` (always, even on failure).

