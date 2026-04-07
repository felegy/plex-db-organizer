---
description: "Use when adding, editing, or reviewing services, data access, or business logic in PlexTmdbSync.Core. Covers singleton registration, Dapper conventions, upsert pattern, logging, and TMDB rate limiting."
applyTo: "src/PlexTmdbSync.Core/**"
---

# Core Layer Rules

All business logic, data access, and external integrations live here. Keep this layer framework-agnostic — no ASP.NET or CLI dependencies.

## Adding a New Service

1. Create a `public class MyService` in `src/PlexTmdbSync.Core/`.
2. Inject dependencies via constructor — always include `ILogger<MyService>`.
3. Register it as a **singleton** in `ServiceCollectionExtensions.cs`:
   ```csharp
   services.AddSingleton<MyService>();
   ```
   Never register services as `Transient` or `Scoped` without explicit justification.

## Data Access

- Use **Dapper** with raw SQL. Do not introduce Entity Framework or any other ORM.
- Always use `SQLiteConnection` from `System.Data.SQLite`.
- Wrap multi-statement writes in a transaction:
  ```csharp
  using var tx = connection.BeginTransaction();
  // ... execute statements ...
  tx.Commit();
  ```
- **Upsert** with `INSERT … ON CONFLICT DO UPDATE SET` — never `DELETE` + `INSERT`.
- Pass parameters as anonymous objects, never via string interpolation.

## Database Migrations

- Migrations are defined as `Migration(int Version, string Description, string Sql)` records in `AppDatabaseService.cs`.
- Add new migrations as numbered entries at the end of the `Migrations` list; never reorder or renumber existing entries.
- Use `IF NOT EXISTS` / `IF EXISTS` guards in DDL so migrations are safe to re-run.
- The `schema_migrations` table tracks applied versions — never write to it outside `AppDatabaseService`.

## Logging

- Inject `ILogger<T>` — never use `Console.Write`, `Debug.Write`, or static loggers.
- Use structured logging with named properties:
  ```csharp
  _logger.LogInformation("Processed {Count} movies in {ElapsedMs}ms", count, elapsed);
  ```

## TMDB API Calls

- All TMDB calls go through `TmdbService`. Do not add `HttpClient` to other services.
- Preserve the **500 ms delay** between TMDB requests — add `await Task.Delay(500)` when adding new API calls in `TmdbService`.

## Shared Types

- DTOs shared across projects belong in `PlexTmdbSync.Types`, not here.
- Internal models used only within Core can be private `record` types inside the relevant service class.
