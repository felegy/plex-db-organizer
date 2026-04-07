---
description: "Use when adding a database migration, changing the schema, adding columns, creating tables, or adding indexes to the app database. Covers migration numbering, SQLite DDL conventions, and the schema_migrations table."
---

# Database Migration Guidelines

All schema changes go through the `Migrations` list in [AppDatabaseService.cs](../../src/PlexTmdbSync.Core/AppDatabaseService.cs). Never run `ALTER TABLE` or other DDL outside of a migration.

## Adding a Migration

1. Read the existing `Migrations` list to find the current highest version number.
2. Append a new entry **at the end** — never reorder, renumber, or edit existing entries:
   ```csharp
   new(
       3,
       "Add WatchedAtUtc column to movies",
       @"ALTER TABLE movies ADD COLUMN WatchedAtUtc TEXT NULL;"
   ),
   ```
3. Description rules: ≤ 60 characters, sentence case, no trailing period.

## SQLite DDL Checklist

| Operation | Required guard |
|-----------|---------------|
| `CREATE TABLE` | `IF NOT EXISTS` |
| `CREATE INDEX` | `IF NOT EXISTS` |
| `DROP TABLE` | `IF EXISTS` |
| `DROP INDEX` | `IF EXISTS` |
| `ALTER TABLE ADD COLUMN` | No guard available — make column `NULL` so re-runs don't break existing rows |

SQLite does **not** support `DROP COLUMN` before version 3.35. For destructive changes, create a new table + `INSERT INTO … SELECT` + rename pattern instead.

## Column Type Mapping

| C# type | SQLite type |
|---------|------------|
| `string` | `TEXT` |
| `int` / `long` | `INTEGER` |
| `double` / `float` | `REAL` |
| `DateTime` (stored as ISO 8601) | `TEXT` |
| nullable → | append `NULL`; non-nullable → `NOT NULL` |

## What NOT to Touch

- The `schema_migrations` table is managed exclusively by `AppDatabaseService.InitializeAsync()`. Never write to it directly.
- The Plex database (`com.plexapp.plugins.library.db`) is **read-only** — never apply migrations to it.
- Do not modify the migration runner logic (`InitializeAsync`) when adding a migration.
