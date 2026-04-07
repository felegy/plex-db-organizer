---
description: "Use when adding, editing, or reviewing API endpoints, routes, or request/response types in PlexTmdbSync.Api. Covers Minimal API conventions, service injection, Swagger metadata, and the no-logic boundary."
applyTo: "src/PlexTmdbSync.Api/**"
---

# API Layer Rules

`Program.cs` is the **only** file in this project that matters for endpoints. It must stay thin.

## No Business Logic

- All logic belongs in `src/PlexTmdbSync.Core/`. `Program.cs` only wires routes to services.
- Never query a database, call TMDB, or compute results directly inside a route handler.
- If a required operation doesn't exist on a service yet, add it to the correct Core service first.

## Endpoint Pattern

Every endpoint must follow this structure:

```csharp
app.MapGet("/route", async (SomeService svc, QueryParam? param) =>
{
    var result = await svc.DoWorkAsync(param);
    return Results.Ok(result);
})
    .WithName("UniqueName")           // PascalCase, unique across all endpoints
    .WithSummary("One-line summary")  // shown in Swagger endpoint list
    .WithDescription("Full sentence explanation of what this endpoint does.")
    .Produces(StatusCodes.Status200OK);
```

## Service Injection

Inject services as **lambda parameters** — never use `app.Services.GetRequiredService<T>()`.

Available services:
| Service | Responsibility |
|---------|---------------|
| `MovieSyncService` | Migrate + full sync pipeline |
| `AppDatabaseService` | Query/save movies in app DB |
| `MovieSearchService` | Fuzzy CSV search |
| `CsvExportService` | Export movies to CSV |
| `PlexDatabaseService` | Read Plex DB (read-only) |
| `TmdbService` | TMDB API calls |

## Response Conventions

- `200 OK` → `Results.Ok(new { ... })` with an anonymous object or typed DTO
- `404 Not Found` → `Results.NotFound()`
- `400 Bad Request` → `Results.BadRequest("reason")`
- Return counts and output paths on mutation endpoints (see `/sync` for example)

## Placement

Add new `app.Map*()` calls after all existing endpoints, immediately before `app.Run()`.
