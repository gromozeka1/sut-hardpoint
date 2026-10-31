# HardPoint SUT — architecture

This repository is the **system under test**: a PC-hardware product-management application made of
an ASP.NET Core MVC storefront and an ASP.NET Core REST API over EF Core + SQLite. It is the only
repository that owns application behaviour and the `api/products` contract.

Two sibling repositories are not part of this one:

| Repo | Relationship |
|---|---|
| [hardpoint-tests](https://github.com/gromozeka1/hardpoint-tests) | BDD suite that drives the published Docker images over HTTP and a browser |
| [playwright-dotnet-mstest-reqnroll](https://github.com/gromozeka1/playwright-dotnet-mstest-reqnroll) | The `HardPoint.Testing` framework package the suite is built on |

Start with [README.md](README.md) for commands; this file explains shape and boundaries.

---

## 1. Projects

Solution `HardPoint.slnx`, .NET 10, SDK pinned to `10.0.100` by `global.json`
(`rollForward: latestFeature`).

| Project | Type | Role |
|---|---|---|
| `WebApp/` | ASP.NET Core MVC | Storefront at `http://localhost:5168` (Docker: `webapp:8080`). Product list with search/filter, create, edit, details, delete. |
| `ProductAPI/` | ASP.NET Core Web API | REST service at `http://localhost:5044` (Docker: `productapi:8080`), SQLite persistence, migrations, seed data, generated OpenAPI. |
| `ProductAPI.Tests/` | xUnit | Controller behaviour, query filters, validation, conflict handling. Each test gets its own in-memory SQLite database, so real EF query translation is exercised. |
| `WebApp.Tests/` | xUnit | Controller actions, `ModelState`/`TempData` handling, the `ProductApiClient` HTTP contract (fake `HttpMessageHandler`), view-model validation, a 503 graceful-degradation case, and a `TestServer` integration suite driving real routing, model binding, antiforgery and Razor rendering against a stubbed API. |

Supporting files: `Directory.Packages.props` (Central Package Management), `global.json`,
`nuget.config` (nuget.org only), `docker-compose.yml` + two service Dockerfiles,
`.github/workflows/dotnet.yml`.

## 2. The isolation boundary

```
Browser ──▶ WebApp (MVC, :5168)
              │  typed HttpClient, base URL from ProductApi:BaseUrl
              ▼
           ProductAPI (Web API, :5044)
              │  EF Core
              ▼
           SQLite ── Database/ProductList.db  (Docker volume: productapi-data)
```

There is **no compile-time reference** between `WebApp` and `ProductAPI`, and `WebApp` never opens
a database connection. The API is the single source of truth; the UI is a projection of it.

`WebApp/Services/ProductApiClient.cs` is the only code that performs HTTP calls. It is registered
in `WebApp/Program.cs`, takes its base URL from configuration, and forwards cancellation tokens
through the whole chain. Every route, payload or status-code change must land on the controller
and this client in the same change.

## 3. API contract

Base URL (local): `http://localhost:5044`

| Method | Path | Success | Other outcomes |
|---|---|---|---|
| GET | `/` | `200 "Hello world!"` (health probe) | — |
| GET | `api/products?search=ryzen&type=CPU` | `200 [Product]` | `400` unknown `type` |
| GET | `api/products/{id}` | `200 Product` | `404` |
| POST | `api/products` | `201 Created` | `400` validation, `409` duplicate name |
| PUT | `api/products/{id}` | `204 No Content` | `400` id mismatch/validation, `404`, `409` duplicate name |
| DELETE | `api/products/{id}` | `204 No Content` | `404` |

- `search` is a case-insensitive substring match on `Name`; `type` is an exact `ProductType`
  match; both combine with AND; no matches returns `200 []`, never `404`.
- JSON is camelCase; **enum values serialize as numbers**.
- Duplicate names return `409` with an RFC 7807 `ProblemDetails` body (`Title`, `Detail`,
  `Status`) — not an opaque `500`. The UI surfaces `Detail` as a `ModelState` error.
- `[ApiController]` + data annotations enforce the `400` paths: `Name` required and ≤ 100 chars,
  `Description` ≤ 1 000, `Price`/`Stock` ≥ 0.

Treat this table as stable: `ProductApiClient` and the external test suite both depend on it.

## 4. Data, migrations, seeding

- SQLite file: `<ContentRoot>/Database/ProductList.db`, resolved in `ProductAPI/Program.cs`. In
  Docker the directory is backed by the `productapi-data` volume.
- Startup calls `Database.Migrate()` ([ProductAPI/Program.cs:28](ProductAPI/Program.cs#L28)). Pending
  migrations in `ProductAPI/Migrations/` are applied before the app serves requests.
- The three seed rows ship **inside the initial migration** and are inserted once, when the
  database is first created. Re-seeding therefore requires `docker compose down -v` or deleting the
  `.db` file — a restart alone does not re-insert them.

| Id | Name | ProductType | Price | Stock |
|---|---|---|---|---|
| 1 | Ryzen 7 7800X3D | CPU | 399 | 25 |
| 2 | Dell UltraSharp 27 | MONITOR | 499 | 5 |
| 3 | Logitech MX Master 3S | PERIPHARALS | 99 | 0 |

The stock values deliberately cover all three badge states (≥ 10, 1–9, 0).

- Never commit `.db` files (gitignored).
- `ProductAPI/ProductAPI.json` is regenerated on every build by
  `Microsoft.Extensions.ApiDescription.Server` (`OpenApiDocumentsDirectory = .`). It is a committed
  build artifact: regenerate, never hand-edit.

## 5. `ProductType` exists twice — the one deliberate wart

| Location | Values |
|---|---|
| `ProductAPI/Models/ProductType.cs` | `CPU`, `MONITOR`, `PERIPHARALS`, `EXTERNAL` |
| `WebApp/Models/ProductViewModel.cs` | `CPU`, `MONITOR`, `PERIPHERALS`, `EXTERNAL` |

`PERIPHARALS` vs `PERIPHERALS` is pre-existing. Do **not** correct it in one place only: the
serialized JSON values, the seed data, persisted rows and every consumer (including the test
suite's mirrored models) must change together.

## 6. Configuration

| Setting | Default | Purpose |
|---|---|---|
| `ProductApi:BaseUrl` (`ProductApi__BaseUrl`) | `http://localhost:5044/` | WebApp → ProductAPI base URL |
| `ASPNETCORE_ENVIRONMENT` | `Development` in Compose | Development logging; enables OpenAPI at `/openapi/v1.json` |

Never hard-code hosts or developer-specific paths. Inside Compose the WebApp must use
`http://productapi:8080/` — service names are DNS names on the Compose network, so `localhost`
inside the stack points at the WebApp container itself.

## 7. Containers

- `docker compose up --build -d` builds both services; `down` stops them keeping the volume;
  `down -v` also deletes the volume (reseed on next start). Validate edits with
  `docker compose config --quiet`.
- Published images: [`gromozeka1/hardpoint-productapi`](https://hub.docker.com/r/gromozeka1/hardpoint-productapi)
  and [`gromozeka1/hardpoint-webapp`](https://hub.docker.com/r/gromozeka1/hardpoint-webapp), tagged
  `latest` (every push to `main`), `sha-<shortsha>`, and `vX.Y.Z`.
- Both containers run as a **non-root user and listen on container port 8080**; the host ports
  `5044`/`5168` are mappings.
- Without Docker: `dotnet run --project ProductAPI` and `dotnet run --project WebApp` in two
  terminals (the `http` launch profile matches the documented URLs).

## 8. Build and verify

```sh
dotnet build HardPoint.slnx            # must be 0 warnings, 0 errors
dotnet test HardPoint.slnx             # both suites
dotnet test ProductAPI.Tests/ProductAPI.Tests.csproj -p:CollectCoverage=true
dotnet test WebApp.Tests/WebApp.Tests.csproj -p:CollectCoverage=true
```

- **Coverage gate**: coverlet is configured per test `.csproj` with a **70% line-coverage
  threshold**; a run fails below it. Reports land in `artifacts/coverage/productapi/` and
  `artifacts/coverage/webapp/` (`coverage.cobertura.xml`, `coverage.json`).
- Excluded from the metric: test assemblies, source-generated OpenAPI code, EF Core migrations and
  Razor view code (views execute only through the HTTP pipeline, which `build-and-smoke` covers —
  hence currently ≈80% ProductAPI and ≈82% WebApp).
- When Docker is available, prove the change the way CI does: `docker compose up -d --build --wait
  --wait-timeout 180`, then assert the WebApp home page renders, the API health probe answers, and
  `GET api/products/1` returns the seed product. Report exactly which checks ran — a
  `docker compose config --quiet` is a config validation, never an integration test.

## 9. CI (`.github/workflows/dotnet.yml`)

Triggered on push and pull request to `main`, three parallel jobs:

| Job | What it does |
|---|---|
| `unit-tests` | Restore + build `HardPoint.slnx` (Release), `dotnet test HardPoint.slnx` |
| `coverage` | Runs each suite with `-p:CollectCoverage=true`, fails below 70%, uploads `artifacts/coverage/` even on failure |
| `build-and-smoke` | Validates the Compose file, builds, launches the stack with `--wait`, smoke-tests home page + health probe + seeded product, always tears down |

## 10. Feature specs (historical)

`.kiro/specs/sut-features/` holds a requirements → design → tasks triad for five enhancements that
are **already implemented**: API validation, client-side form validation, the `Stock` field with
list/detail badges, product search/filter, and duplicate-name prevention. All 41 tasks are checked
complete and the code matches (migrations, `Stock` column, `_StockBadge` partial, query
parameters). Read it as a design record, not as current-state documentation — for current
behaviour, trust the code and `README.md`.

## 11. Conventions

- **Central Package Management.** Versions live only in `Directory.Packages.props`
  (`CentralPackageTransitivePinningEnabled = true`). Never put a `Version` attribute on a
  `PackageReference` in a `.csproj`.
- **Style** (`.editorconfig`): 4 spaces for C#, 2 for JSON/Markdown/YAML, LF everywhere,
  file-scoped namespaces, nullable + implicit usings enabled, braces on new lines. `PascalCase`
  types/public members, `_camelCase` private fields, `I` prefix for interfaces.
- **Commits**: imperative and concise. Do not commit `bin/`, `obj/`, SQLite `.db` files,
  screenshots or report output.

## 12. Where else to read

| Question | File |
|---|---|
| Commands, ports, API reference, Compose usage | [README.md](README.md) |
| Agent-facing rules and verification checklist | [AGENTS.md](AGENTS.md), [CLAUDE.md](CLAUDE.md) |
| Dependency flow and design patterns | [.kiro/steering/architecture.md](.kiro/steering/architecture.md) |
| Endpoints, seed data, Compose, CI detail | [.kiro/steering/sut-and-deployment.md](.kiro/steering/sut-and-deployment.md) |
| The five feature specs | [.kiro/specs/sut-features/](.kiro/specs/sut-features/) |
