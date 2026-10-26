# HardPoint — Services & Deployment

## Services

| Service | Local URL | Docker internal | Description |
|---|---|---|---|
| WebApp | `http://localhost:5168` | `webapp:8080` | ASP.NET Core MVC — product list with search/filter, create, edit, details, delete |
| ProductAPI | `http://localhost:5044` | `productapi:8080` | ASP.NET Core Web API — `GET/POST/PUT/DELETE api/products` |

WebApp proxies all data through ProductAPI via `ProductApiClient` (typed `HttpClient`). The MVC app has no direct database access.

## ProductAPI Endpoints

```
GET    api/products?search=&type=   → 200 []Product | 400 (unknown type)
GET    api/products/{id}            → 200 Product | 404
POST   api/products                 → 201 Product | 409 (name already exists)
PUT    api/products/{id}            → 204 | 400 (id mismatch) | 404 | 409 (name already exists)
DELETE api/products/{id}            → 204 | 404
GET    /                            → 200 "Hello world!" (health probe)
```

`search` matches `Name` (case-insensitive substring), `type` matches `ProductType`.

Persistence: SQLite at `<ContentRoot>/Database/ProductList.db`. Seed data created by `ProductContext.OnModelCreating`:

| Id | Name | ProductType | Price | Stock |
|---|---|---|---|---|
| 1 | Ryzen 7 7800X3D | CPU | 399 | 25 |
| 2 | Dell UltraSharp 27 | MONITOR | 499 | 5 |
| 3 | Logitech MX Master 3S | PERIPHARALS | 99 | 0 |

Seed data is applied via `Database.EnsureCreated()` at startup — it is only inserted once when the database file is first created. Anything that reads seed product id `1` therefore depends on a fresh database; re-seed with `docker compose down -v` or by deleting the `.db` file.

## ProductType Enum

Defined in two places — keep them in sync when adding new values:

| Location | Values |
|---|---|
| `ProductAPI/Models/ProductType.cs` | `CPU`, `MONITOR`, `PERIPHARALS`, `EXTERNAL` |
| `WebApp/Models/ProductViewModel.cs` | `CPU`, `MONITOR`, `PERIPHERALS`, `EXTERNAL` |

Note: `PERIPHARALS` (API) vs `PERIPHERALS` (WebApp) is an existing inconsistency. Do not silently correct it without updating both files, the seed data and any persisted database rows.

## Running Locally

```powershell
# 1. Build
dotnet build HardPoint.slnx

# 2. Start services with Docker
docker compose up --build -d

# or run each project directly, in two terminals
dotnet run --project ProductAPI    # http://localhost:5044
dotnet run --project WebApp        # http://localhost:5168
```

## Exploring the running app in a browser

Use the `playwright-cli` skill to open and inspect the running WebApp and ProductAPI manually. The `.playwright/cli.config.json` file is pre-configured for this project (Chromium, `localhost:5168`, `slowMo: 500`), so no flags are needed for most commands.

**Install once:**
```bash
npm install -g @playwright/cli@latest
playwright-cli install --skills
```

**Open the WebApp:**
```bash
playwright-cli open http://localhost:5168
playwright-cli snapshot          # see the page structure with element refs
playwright-cli find "Products"   # locate specific text in the snapshot
```

**Inspect the ProductAPI directly (named sessions keep both open):**
```bash
playwright-cli -s=webapp open http://localhost:5168
playwright-cli -s=api    open http://localhost:5044/api/products
playwright-cli list
playwright-cli close-all
```

**Take a screenshot for a bug report or PR:**
```bash
playwright-cli open http://localhost:5168/Products
playwright-cli screenshot --filename=products-list.png
```

## Playwright MCP

The Playwright MCP server (`@playwright/mcp`) is configured at the workspace level and gives any MCP-capable client (Kiro, Claude Desktop, VS Code Copilot, Cursor, etc.) a persistent browser it can drive via structured tool calls.

**Configuration files:**
- `.kiro/settings/mcp.json` — registers the server with Kiro using the `servers` key (schema uses `servers`, not `mcpServers`); points to the project config file
- `.playwright/mcp.config.json` — MCP server options: Chromium, isolated profile, `csharp` codegen, outputs to `TestResults/mcp/`

**Capabilities enabled** (in `.playwright/mcp.config.json`):
- `network` — inspect and mock HTTP requests
- `storage` — read/write cookies, localStorage, sessionStorage
- `testing` — web-first assertions (`browser_expect_*` tools)
- `devtools` — evaluate JavaScript, access console

`isolated: true` keeps the browser profile in memory so no stale cookies or local storage bleed into a new session, and `codegen: "csharp"` produces C# snippets usable directly in this solution's conventions.

## Docker Compose

`docker-compose.yml` defines two services:

- **productapi** — builds `ProductAPI/Dockerfile`; mounts volume `productapi-data:/app/Database` to persist SQLite across restarts; runs with `ASPNETCORE_ENVIRONMENT=Development` (enables OpenAPI at `/openapi/v1.json`).
- **webapp** — builds `WebApp/Dockerfile`; sets `ProductApi__BaseUrl=http://productapi:8080/` to reach the API over the Docker internal network; depends on `productapi`.

```powershell
docker compose up --build -d    # start
docker compose down             # stop (volume is preserved)
docker compose down -v          # stop and delete database volume (resets seed data)
```

## CI Workflow (`.github/workflows/dotnet.yml`)

Trigger: push or PR to `main`. Runner: `ubuntu-latest`, 20-minute timeout.

Key steps:
1. `docker compose config --quiet` — validate the Compose file
2. `dotnet restore HardPoint.slnx` + `dotnet build HardPoint.slnx -c Release`
3. `docker compose up -d --build --wait --wait-timeout 180`
4. Smoke-test: WebApp home renders, API health probe answers, seed product id `1` is returned by `GET api/products/1`, and the WebApp detail page shows it
5. `docker compose down` (always, even on failure)

## WebApp Configuration

`WebApp/appsettings.json`:
```json
{ "ProductApi": { "BaseUrl": "http://localhost:5044/" } }
```

Overridden in Docker Compose with `ProductApi__BaseUrl: http://productapi:8080/` (double-underscore notation for nested keys in ASP.NET Core environment variables).
