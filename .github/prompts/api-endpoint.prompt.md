---
description: "Add a new REST endpoint to the API. Use when: adding a GET or POST route to Program.cs wired to an existing Core service method, following Minimal API conventions."
argument-hint: "Describe the endpoint, e.g. 'GET /stats returning movie count by year'"
agent: "agent"
---

Add a new Minimal API endpoint to [Program.cs](../../src/PlexTmdbSync.Api/Program.cs).

## Task

The user wants to add: **${input:endpoint_description}**

## Rules

1. **No business logic in Program.cs.** All logic must live in a Core service (`src/PlexTmdbSync.Core/`). If the required method doesn't exist on a service, create it there first.
2. **Inject the service** as a lambda parameter — do not use `app.Services.GetRequiredService<T>()`.
3. **Add Swagger metadata** using `.WithName(...)`, `.WithSummary(...)`, `.WithDescription(...)`, and `.Produces(...)` — follow the style of existing endpoints.
4. Use `Results.Ok(...)` for 200 responses and `Results.NotFound()` / `Results.BadRequest(...)` where appropriate.
5. Place the new `app.Map*()` call after the last existing endpoint, before `app.Run()`.
6. Do **not** modify any other project outside `PlexTmdbSync.Api` and `PlexTmdbSync.Core`.
7. If a new service method is needed, add it to the correct existing service — do not create a new service unless the functionality clearly doesn't belong to any existing one.

## Existing services available for injection

- `MovieSyncService` — migrate, full sync pipeline
- `AppDatabaseService` — query/save movies in the app DB
- `MovieSearchService` — fuzzy CSV search
- `CsvExportService` — export movies to CSV
- `PlexDatabaseService` — read from Plex DB (read-only)
- `TmdbService` — TMDB API calls

## Output

1. If a new service method is needed: show the method signature and implementation first, then apply it.
2. Show the full `app.Map*()` block you are adding, then apply it to `Program.cs`.
