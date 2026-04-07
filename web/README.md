# Plex TMDB Sync Web Client - User Help

## What Is This Interface For?

This web interface is a tool for reviewing and maintaining your Plex movie list.

What you can do with it:

- Check API status
- Run database migrations
- Run sync with Plex and TMDB data
- List, filter, and sort movies
- Review search results
- Mark movies for deletion (Must Delete)

## Navigation Sections

### Dashboard

- API health: checks whether the backend is reachable.
- Migrate: runs required database migrations.
- Sync: starts the full sync workflow.
	- Batch size: how many movies are processed per batch.
	- Output path: where the CSV output file is written.
	- Enable TMDB enrichment: adds TMDB data (rating, overview, poster).

### Movies

- Full list of synced movies.
- Filter by title: title filter.
- Sort: sort by title, year, or TMDB rating.
- Mark must-delete: flags a movie for deletion.
- TMDB link: opens the movie page on TMDB.

### Search

- Fuzzy search across CSV data.
- Search term: the text you want to search for.
- Threshold (0-100): higher values require stricter matching.
- Extended search: searches additional fields (for example overview and genres).
- Search results can be managed just like the Movies view:
	- TMDB link
	- Mark must-delete

### Must Delete

- List of all movies marked for deletion.
- Use this view to quickly review currently flagged items.

## Recommended Workflow

1. Dashboard: Check health
2. Dashboard: Run migrate
3. Dashboard: Run sync
4. Movies or Search: mark movies that should be deleted
5. Must Delete: verify all flagged items

## Interface Feedback

- Green status/notification: successful operation.
- Red status/error: something failed.
- Status messages disappear automatically, but you can close them manually.

## Important Notes

- API base URL settings are in a collapsible panel on Dashboard.
- The selected API URL is saved in your browser.
- The panel open/closed state is also remembered.

## Quick Troubleshooting

- If the list is empty: run Sync again.
- If search returns no results: lower the Threshold value.
- If API errors appear: Dashboard > Check health, then verify the API URL.
