---
description: "Scaffold a new numbered SQLite migration in AppDatabaseService.cs. Use when: adding a column, creating a table, or adding an index to the app database."
argument-hint: "Describe the schema change, e.g. 'add WatchedAtUtc column to movies'"
agent: "agent"
---

Add a new database migration to [AppDatabaseService.cs](../../src/PlexTmdbSync.Core/AppDatabaseService.cs).

## Task

The user wants to: **${input:migration_description}**

## Rules

1. Read the current `Migrations` list in `AppDatabaseService.cs` to determine the next version number (increment the highest existing version by 1).
2. Append a new `Migration` entry to the `Migrations` list using the existing `new(version, description, sql)` record syntax.
3. Write valid SQLite DDL in the `Sql` field. Use `IF NOT EXISTS` / `IF EXISTS` guards where appropriate.
4. Keep the description concise (≤ 60 chars), in sentence case, without a trailing period.
5. Do **not** modify any other methods, the upsert SQL, or any project outside `PlexTmdbSync.Core`.
6. Do **not** use Entity Framework — raw SQL only.

## Output

Show the exact `new(...)` entry you are adding, then apply the edit to the file.
