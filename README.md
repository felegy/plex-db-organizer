# Plex TMDB Sync Solution

A multi-project .NET solution for syncing Plex movie metadata, enriching it with TMDB data, storing it in an app database, exporting CSV, and searching results.

## Solution Structure

```text
plex-db/
├── plex-db.sln
├── .env
├── assets/
│   ├── csv/
│   │   └── plex_movies.csv
│   └── db/
│       ├── com.plexapp.plugins.library.db
│       └── plex_movies_app.db
└── src/
    ├── PlexTmdbSync.Types/     # Shared models/types
    ├── PlexTmdbSync.Core/      # Business logic + data access (Dapper)
    ├── PlexTmdbSync.ApiHost/   # Shared API host module (endpoints, middleware, web client serving)
    ├── PlexTmdbSync.Cli/       # Command-line client
    └── PlexTmdbSync.Api/       # REST API
```

## Projects

- `PlexTmdbSync.Types`
  - Shared DTOs/models (`PlexMovie`, TMDB response models, search result model).

- `PlexTmdbSync.Core`
  - Business logic and data access.
  - Plex DB reader (Dapper + SQLite).
  - App DB migrations/versioning (`schema_migrations`).
  - TMDB enrichment service (with IMDb ID extraction).
  - OMDb enrichment service (IMDb ratings and metadata).
  - CSV export service.
  - Fuzzy search service.
  - Sync orchestration service.

- `PlexTmdbSync.ApiHost`
  - Shared host module used by both `Api` and `Cli`.
  - All API endpoints under `/api` route prefix.
  - Swagger UI served at `/api/swagger`.
  - Serves web client static files from `/app`.

- `PlexTmdbSync.Cli`
  - Console entrypoint.
  - Supports sync, migration-only, CSV search, and MustDelete management modes.
  - Can host the API server (embeds `ApiHostModule`).

- `PlexTmdbSync.Api`
  - Thin REST API entrypoint; delegates entirely to `ApiHostModule`.
  - Serves both API (`/api`) and web client (`/app`) from a single Kestrel host.

## Requirements

- .NET 10 SDK
- Node.js 20+ (for the web client)
- Plex SQLite DB file
- TMDB credentials in `.env` (required only when TMDB enrichment is enabled)

## Environment Variables

Core variables in `.env`:

- `PLEX_DATABASE_PATH`
- `DATABASE_URL` (app DB connection)

TMDB variables (required when running TMDB-enriched sync):

- `TMDB_API_KEY`
- `TMDB_API_BASE_URL`
- `TMDB_ACCESS_TOKEN`

OMDb variables (required when running OMDb-enriched sync):

- `OMDB_API_KEY`
- `OMDB_API_BASE_URL`

Optional web-client CORS variables:

- `WEB_CLIENT_ORIGIN` (single origin)
- `WEB_CLIENT_ORIGINS` (comma-separated origins)

Examples for `DATABASE_URL`:

- `sqlite://assets/db/plex_movies_app.db`
- `sqlite:///home/data/felegy/plex-db/assets/db/plex_movies_app.db`
- `Data Source=/home/data/felegy/plex-db/assets/db/plex_movies_app.db;Version=3;`

## Build

```bash
dotnet restore
dotnet build plex-db.sln
```

## Run Locally

Start the API locally:

```bash
dotnet run --project src/PlexTmdbSync.Api/PlexTmdbSync.Api.csproj
```

Local URLs:

- API base URL: `http://localhost:5242/api`
- Health check: `http://localhost:5242/api/health`
- Swagger UI: `http://localhost:5242/api/swagger`
- Swagger JSON: `http://localhost:5242/api/swagger/v1/swagger.json`
- Web client: `http://localhost:5242/app`

The API server also serves the built web client from `/app`. The web client automatically uses
`window.location.origin/api` as the API base URL when served from the same host.

For local web client development (live reload with Parcel):

```bash
npm install
npm start
```

Web client dev URL:

- `http://localhost:1234`
- Override API base URL in the UI settings panel (default auto-detects to `localhost:1234/api`
  which won't work — set it manually to `http://localhost:5242/api`)

## Docker Compose Test Environment

The repository uses two compose files:

- `compose.yaml`: base services using published images (`ghcr.io/felegy/...`)
- `compose.override.yaml`: local development overrides (build, ports, env file, volumes)

With Docker Compose defaults, `compose.override.yaml` is loaded automatically.

Start API in Docker (local build via override):

```bash
docker compose up --build -d api
```

The override maps container port `8080` to host port `5242`, so the API is available on:

- API: `http://localhost:5242/api`
- Health check: `http://localhost:5242/api/health`
- Swagger UI: `http://localhost:5242/api/swagger`
- Web client: `http://localhost:5242/app`

Check health:

```bash
curl http://localhost:5242/api/health
```

Run one-off CLI tasks in Docker:

```bash
docker compose run --rm cli --migrate-only
docker compose run --rm cli --search "batman"
docker compose run --rm cli --mark-must-delete 123
docker compose run --rm cli --list-must-delete
```

Stop containers:

```bash
docker compose down
```

To test only published images (without local build overrides):

```bash
docker compose -f compose.yaml up -d api
docker compose -f compose.yaml --profile tools run --rm cli --migrate-only
```

## CLI Usage

The CLI uses System.CommandLine with built-in validation and help output.

Run CLI:

```bash
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --tmdb false
```

Options:

```text
--output <path>     Output CSV path (default: assets/csv/plex_movies.csv)
--tmdb <true|false> Enable TMDB enrichment (default: true)
--omdb <true|false> Enable OMDb enrichment for IMDb metadata (default: true)
--batch <number>    Batch size for external API calls (default: 10)
--search <term>     Search movies in CSV (Prefer: client search --term)
--threshold <num>   Minimum fuzzy score for search (default: 60)
--search-extended   Extend search to Summary, Genres, FilePath, and TmdbOverview
--sync              Run full sync explicitly (Prefer: client sync)
--migrate-only      Run app database migrations only, then exit (Prefer: client migrate)
--mark-must-delete  Mark one movie by id as MustDelete (Prefer: client movies must-delete mark --id)
--list-must-delete  List movies marked as MustDelete (Prefer: client movies must-delete list)
--version           Show version information
--help              Show command help and available options
```

Commands:

```text
api                 Run API server from the CLI
client              REST API client commands
```

Client subcommands:

```text
health              Call GET /health
migrate             Call POST /migrate
sync                Call POST /sync
movies list         Call GET /movies
movies must-delete list
movies must-delete mark --id <id>
search              Call GET /search
```

API command options:

```text
--urls <urls>       ASPNETCORE_URLS override for the API server
```

Client command options:

```text
--api-url <url>     Base URL for REST API client requests
--json              Output raw JSON instead of formatted table
--verbose           Print HTTP request/response method, URL, and status to stderr
```

Client sync options:

```text
--tmdb-enrich       Enable TMDB enrichment (default: true)
--omdb-enrich       Enable OMDb enrichment for IMDb metadata (default: true)
--batch-size <num>  Batch size for sync request (default: 10)
--output-path <p>   Optional CSV output path for sync request
```

Client search options:

```text
--term <term>       Search term (required)
--output-path <p>   CSV path to search in (default: assets/csv/plex_movies.csv)
--threshold <num>   Minimum fuzzy score (0–100, default: 60)
--extended          Search in Summary, Genres, FilePath, and TmdbOverview
```

Client URL precedence:

1. `client --api-url ...`
2. `API_BASE_URL` environment variable
3. `ASPNETCORE_URLS` environment variable (first URL, `/api` appended)
4. Default: `http://localhost:5242/api`

API URL precedence:

1. `api --urls ...`
2. `ASPNETCORE_URLS` environment variable
3. Default: `http://localhost:5242`

Help examples:

```bash
# CLI help (local)
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --help

# CLI help (docker, force rebuild so latest parser changes are included)
docker compose run --rm --build cli --help
```

Examples:

```bash
# Sync without TMDB
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --tmdb false

# Full sync with TMDB
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --tmdb true --batch 10

# Full sync explicitly (docker)
docker compose run --rm --build cli --sync

# Start API server from the CLI
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- api

# Start API server from the CLI with custom URL
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- api --urls http://localhost:5250

# Start API server from the CLI using ASPNETCORE_URLS
ASPNETCORE_URLS=http://+:8080 dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- api

# Client health check using default API URL resolution
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client health

# Client health check with explicit API URL
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api health

# Client migrate request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api migrate

# Client sync request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api sync --tmdb-enrich true --batch-size 50

# Client movies list request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api movies list

# Client must-delete list request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api movies must-delete list

# Client must-delete mark request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api movies must-delete mark --id 123

# Client search request (formatted table)
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api search --term "batman"

# Client search with extended fields and lower threshold
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api search --term "gotham" --extended --threshold 40

# Client search with raw JSON output
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api --json search --term "alien"

# Client health check with verbose HTTP logging
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080/api --verbose health

# Search in CSV
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --search "batman"

# Search with extended fields
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --search "gotham" --search-extended

# Run DB migrations only
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --migrate-only

# Mark one movie as MustDelete
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --mark-must-delete 123

# List movies marked as MustDelete
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --list-must-delete
```

## REST API Usage

Run API:

```bash
dotnet run --project src/PlexTmdbSync.Api/PlexTmdbSync.Api.csproj
```

Endpoints:

- `GET /api/health`
- `POST /api/migrate`
- `POST /api/sync`
- `GET /api/movies`
- `POST /api/movies/{id}/must-delete`
- `GET /api/movies/must-delete`
- `GET /api/search?term=...&outputPath=...&threshold=...&extended=...`
- `GET /api/swagger/v1/swagger.json`
- `GET /api/swagger`

Swagger UI is available at `/api/swagger`. The generated OpenAPI document is available at `/api/swagger/v1/swagger.json`.

The web client is served from `/app` by the same Kestrel host.

MustDelete examples:

```bash
# Mark one movie as MustDelete
curl -X POST http://localhost:5242/movies/123/must-delete

# List movies currently marked as MustDelete
curl http://localhost:5242/movies/must-delete
```

Example sync request:

```bash
curl -X POST http://localhost:5242/sync \
  -H "Content-Type: application/json" \
  -d '{"tmdbEnrich": true, "batchSize": 10, "outputPath": "assets/csv/plex_movies.csv"}'
```

## App Database Migrations

The app database uses migration versioning in `schema_migrations`.

- Migrations are versioned and tracked.
- Pending migrations run automatically at startup.
- Migrations run inside a transaction.

Current migrations:

1. Create `movies` table.
2. Add indexes (`Title`, `OriginalTitle`, `TmdbId`).
3. Add `MustDelete` column with default `false` (`0`).

## Logging

Both CLI and API use Serilog with compact JSON output suitable for Kubernetes log collectors.
