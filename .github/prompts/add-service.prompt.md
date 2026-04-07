---
description: "Scaffold a new Core service in PlexTmdbSync.Core. Use when: adding a new service class, registering it in the DI container, or implementing a new area of business logic."
argument-hint: "Describe the service, e.g. 'WatchHistoryService that records when movies were watched'"
agent: "agent"
---

Add a new service to [PlexTmdbSync.Core](../../src/PlexTmdbSync.Core/).

## Task

The user wants to create: **${input:service_description}**

## Steps

### 1. Create the service file

Create `src/PlexTmdbSync.Core/<ServiceName>.cs` following this pattern:

```csharp
using Microsoft.Extensions.Logging;

namespace PlexTmdbSync.Core;

public class <ServiceName>
{
    private readonly ILogger<<ServiceName>> _logger;
    // inject other Core services as needed

    public <ServiceName>(ILogger<<ServiceName>> logger /*, OtherService other */)
    {
        _logger = logger;
    }

    // public async Task methods here
}
```

Rules:
- Always inject `ILogger<T>` — never use `Console.Write` or static loggers.
- Inject other Core services by constructor parameter, not via `ServiceProvider`.
- Keep the class `public` and non-static.
- If the service needs database access, inject `AppDatabaseService` — never create a new `SQLiteConnection` outside `AppDatabaseService`.
- If the service needs TMDB calls, inject `TmdbService` — never add `HttpClient` to a new service.
- If new shared DTOs are needed, add them to `src/PlexTmdbSync.Types/` first.

### 2. Register in the DI container

Add `services.AddSingleton<ServiceName>();` to [ServiceCollectionExtensions.cs](../../src/PlexTmdbSync.Core/ServiceCollectionExtensions.cs) inside `AddPlexTmdbCore()`, after the last existing `AddSingleton` line.

## Output

1. Show the full new service class, then create the file.
2. Show the `AddSingleton` line being added, then apply it to `ServiceCollectionExtensions.cs`.
