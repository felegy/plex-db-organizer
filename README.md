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
  - Supports sync, migration-only, and CSV search modes.

- `PlexTmdbSync.Api`
  - REST API host around core services.
  - Endpoints for health, migrate, sync, list movies, and search.

## Requirements

- .NET 10 SDK
- Plex SQLite DB file
- TMDB credentials in `.env`

## Environment Variables

Required in `.env`:

- `TMDB_API_KEY`
- `TMDB_API_BASE_URL`
- `TMDB_ACCESS_TOKEN`
- `PLEX_DATABASE_PATH`
- `DATABASE_URL` (app DB connection)

Examples for `DATABASE_URL`:

- `sqlite://assets/db/plex_movies_app.db`
- `sqlite:///home/data/felegy/plex-db/assets/db/plex_movies_app.db`
- `Data Source=/home/data/felegy/plex-db/assets/db/plex_movies_app.db;Version=3;`

## Build

```bash
dotnet restore
dotnet build plex-db.sln
```

## CLI Usage

Run CLI:

```bash
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --tmdb false
```

Options:

```text
--output <path>     Output CSV path (default: assets/csv/plex_movies.csv)
--tmdb <true|false> Enable TMDB enrichment (default: true)
--batch <number>    Batch size for TMDB API calls (default: 10)
--search <term>     Search movies in CSV
--threshold <num>   Minimum fuzzy score for search (default: 60)
--search-extended   Extend search to Summary, Genres, FilePath, and TmdbOverview
--migrate-only      Run app database migrations only, then exit
```

Examples:

```bash
# Sync without TMDB
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --tmdb false

# Full sync with TMDB
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --tmdb true --batch 10

# Search in CSV
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --search "batman"

# Search with extended fields
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --search "gotham" --search-extended

# Run DB migrations only
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -- --migrate-only
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
- `GET /search?term=...&outputPath=...&threshold=...&extended=...`
- `GET /swagger/v1/swagger.json`
- `GET /swagger`

Swagger UI is available at `/swagger`, and the generated OpenAPI document is available at `/swagger/v1/swagger.json`.

Example sync request:

```bash
curl -X POST http://localhost:5000/sync \
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

## Logging

Both CLI and API use Serilog with compact JSON output suitable for Kubernetes log collectors.
