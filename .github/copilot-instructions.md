# Plex TMDB Sync — Copilot Instructions

## Architecture

Four-project .NET 10 solution with a strict layering rule:

```
PlexTmdbSync.Types   → shared DTOs only (PlexMovie, TmdbMovieResponse, MovieSearchResult)
PlexTmdbSync.Core    → all services, data access, business logic
PlexTmdbSync.Cli     → CLI entrypoint, no business logic
PlexTmdbSync.Api     → REST API entrypoint, no business logic
```

**Dependency direction:** `Cli`/`Api` → `Core` → `Types`. Never add business logic to `Cli` or `Api`.

All services live in `src/PlexTmdbSync.Core/` and are registered as singletons via `AddPlexTmdbCore()` in `ServiceCollectionExtensions.cs`. Add new services there.

## Build and Run

```bash
dotnet build                                                          # whole solution
dotnet run --project src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj    # CLI (full sync)
dotnet run --project src/PlexTmdbSync.Api/PlexTmdbSync.Api.csproj    # API (http://localhost:5242)
```

No test projects exist yet.

## Conventions

- **Nullable + ImplicitUsings** are enabled in all projects — no need for explicit `using` directives for BCL types.
- **Singletons everywhere** — all Core services are `Singleton`. Only deviate with explicit justification.
- **Dapper over EF** — data access uses raw SQL with Dapper. Do not introduce Entity Framework.
- **Database migrations** are version-tracked via a `schema_migrations` table in the app DB. Add new migrations as numbered entries inside `AppDatabaseService.cs` using the existing pattern.
- **Upsert pattern** — `INSERT … ON CONFLICT DO UPDATE` (SQLite) for saving movies; do not use `DELETE`+`INSERT`.
- **TMDB rate limiting** — `TmdbService` sleeps 500 ms between requests; preserve this when adding new API calls.
- **FuzzySharp thresholds** — default fuzzy threshold is 60 (0–100 scale). The `--threshold` CLI flag and `?threshold=` query param override it.
- **Serilog** — use `ILogger<T>` injection everywhere; configure sinks only in `Cli/Program.cs` and `Api/Program.cs`.

## Configuration

All runtime config comes from `.env` (auto-loaded at startup). Required variables:

| Variable | Purpose |
|----------|---------|
| `TMDB_API_KEY` | TMDB API key |
| `TMDB_API_BASE_URL` | TMDB API base URL |
| `TMDB_ACCESS_TOKEN` | TMDB bearer token |
| `PLEX_DATABASE_PATH` | Path to Plex SQLite DB (read-only) |
| `DATABASE_URL` | Path to app SQLite DB |

See `src/PlexTmdbSync.Core/ConfigService.cs` for how variables are read.

## Databases

| Database | File | Access |
|----------|------|--------|
| Plex library | `assets/db/com.plexapp.plugins.library.db` | **Read-only** — never write |
| App database | `assets/db/plex_movies_app.db` | Managed by `AppDatabaseService` |

The Plex DB GUID format is `com.plexapp.agents.themoviedb-{tmdbId}?lang=…` — TMDB ID is extracted from this string.

## CLI Modes

| Flag | Behavior |
|------|----------|
| *(none)* | Full pipeline: migrate → fetch Plex → TMDB enrich → save → export CSV |
| `--migrate-only` | Run DB migrations only |
| `--tmdb false` | Skip TMDB enrichment |
| `--search "term"` | Fuzzy search CSV; skips sync |
| `--search-extended` | Include summary/genres/overview in search |
| `--batch N` | TMDB batch size (default 10) |
| `--output path` | CSV output path (default `assets/csv/plex_movies.csv`) |

## API Endpoints

`GET /health` · `POST /migrate` · `POST /sync` · `GET /movies` · `GET /search`

Swagger UI at `http://localhost:5242/swagger/index.html`.
