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
    ├── PlexTmdbSync.Types/   # Shared models/types
    ├── PlexTmdbSync.Core/    # Business logic + data access (Dapper)
    ├── PlexTmdbSync.Cli/     # Command-line client
    └── PlexTmdbSync.Api/     # REST API
```

## Projects

- `PlexTmdbSync.Types`
  - Shared DTOs/models (`PlexMovie`, TMDB response models, search result model).

- `PlexTmdbSync.Core`
  - Business logic and data access.
  - Plex DB reader (Dapper + SQLite).
  - App DB migrations/versioning (`schema_migrations`).
  - TMDB enrichment service.
  - CSV export service.
  - Fuzzy search service.
  - Sync orchestration service.

- `PlexTmdbSync.Cli`
  - Console entrypoint.
  - Supports sync, migration-only, CSV search, and MustDelete management modes.

- `PlexTmdbSync.Api`
  - REST API host around core services.
  - Endpoints for health, migrate, sync, list movies, MustDelete management, and search.

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

- API base URL: `http://localhost:5242`
- Health check: `http://localhost:5242/health`
- Swagger UI: `http://localhost:5242/swagger`
- Swagger JSON: `http://localhost:5242/swagger/v1/swagger.json`

Start the web client locally (Parcel + Alpine.js):

```bash
npm install
npm start
```

Web client URL:

- `http://localhost:1234`
- Default API base URL in UI: `http://localhost:5242`

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

- `http://localhost:5242`
- `http://localhost:5242/health`
- `http://localhost:5242/swagger`

Check health:

```bash
curl http://localhost:5242/health
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
--batch <number>    Batch size for TMDB API calls (default: 10)
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
3. `ASPNETCORE_URLS` environment variable (first URL)
4. Default: `http://localhost:5242`

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
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 health

# Client migrate request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 migrate

# Client sync request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 sync --tmdb-enrich true --batch-size 50

# Client movies list request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 movies list

# Client must-delete list request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 movies must-delete list

# Client must-delete mark request
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 movies must-delete mark --id 123

# Client search request (formatted table)
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 search --term "batman"

# Client search with extended fields and lower threshold
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 search --term "gotham" --extended --threshold 40

# Client search with raw JSON output
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 --json search --term "alien"

# Client health check with verbose HTTP logging
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- client --api-url http://127.0.0.1:8080 --verbose health

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

- `GET /health`
- `POST /migrate`
- `POST /sync`
- `GET /movies`
- `POST /movies/{id}/must-delete`
- `GET /movies/must-delete`
- `GET /search?term=...&outputPath=...&threshold=...&extended=...`
- `GET /swagger/v1/swagger.json`
- `GET /swagger`

Swagger UI is available at `/swagger`, and the generated OpenAPI document is available at `/swagger/v1/swagger.json`.

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
