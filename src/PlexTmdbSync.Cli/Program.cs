using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlexTmdbSync.Core;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

LoadEnvFile(".env");

var services = new ServiceCollection();

Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Information()
	.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
	.Enrich.FromLogContext()
	.WriteTo.Console(new RenderedCompactJsonFormatter())
	.CreateLogger();

services.AddLogging(builder =>
{
	builder.ClearProviders();
	builder.AddSerilog(Log.Logger, dispose: true);
});

services.AddPlexTmdbCore();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var syncService = serviceProvider.GetRequiredService<MovieSyncService>();
var searchService = serviceProvider.GetRequiredService<MovieSearchService>();

string outputPath = "assets/csv/plex_movies.csv";
bool tmdbEnrich = true;
int batchSize = 10;
string? searchTerm = null;
int searchThreshold = 60;
bool searchExtended = false;
bool migrateOnly = false;

for (int i = 0; i < args.Length; i++)
{
	if (args[i] == "--output" && i + 1 < args.Length)
		outputPath = args[i + 1];
	else if (args[i] == "--tmdb" && i + 1 < args.Length)
		tmdbEnrich = args[i + 1].ToLowerInvariant() != "false";
	else if (args[i] == "--batch" && i + 1 < args.Length)
		int.TryParse(args[i + 1], out batchSize);
	else if (args[i] == "--search" && i + 1 < args.Length)
		searchTerm = args[i + 1];
	else if (args[i] == "--threshold" && i + 1 < args.Length)
		int.TryParse(args[i + 1], out searchThreshold);
	else if (args[i] == "--search-extended")
		searchExtended = true;
	else if (args[i] == "--migrate-only")
		migrateOnly = true;
}

try
{
	logger.LogInformation("Starting Plex TMDB Sync CLI...");

	if (searchTerm is not null)
	{
		var results = await searchService.SearchInCsvAsync(outputPath, searchTerm, searchThreshold, searchExtended);
		if (results.Count == 0)
		{
			Console.WriteLine($"No movies found matching \"{searchTerm}\" (threshold: {searchThreshold}).");
			Console.WriteLine("Tip: lower the threshold with --threshold 40");
			return;
		}

		int titleW = Math.Max(results.Max(x => x.Movie.Title.Length), 5);
		int yearW = 4;
		int scoreW = 5;
		int ratingW = 6;
		int tmdbW = Math.Max(results.Max(x => x.Movie.TmdbId.ToString().Length), 6);
		int urlW = 42;

		string Line() => "+" + new string('-', titleW + 2) + "+" + new string('-', yearW + 2) + "+" + new string('-', scoreW + 2) + "+" + new string('-', ratingW + 2) + "+" + new string('-', tmdbW + 2) + "+" + new string('-', urlW + 2) + "+";
		string Row(string t, string y, string s, string r, string tmdb, string url) =>
			$"| {t.PadRight(titleW)} | {y.PadRight(yearW)} | {s.PadRight(scoreW)} | {r.PadRight(ratingW)} | {tmdb.PadRight(tmdbW)} | {url.PadRight(urlW)} |";

		Console.WriteLine(Line());
		Console.WriteLine(Row("Title", "Year", "Score", "Rating", "TmdbId", "TmdbUrl"));
		Console.WriteLine(Line());
		foreach (var result in results)
		{
			var movie = result.Movie;
			var tmdbUrl = movie.TmdbId > 0 ? $"https://www.themoviedb.org/movie/{movie.TmdbId}" : string.Empty;
			if (tmdbUrl.Length > urlW)
				tmdbUrl = ".." + tmdbUrl[^(urlW - 2)..];

			Console.WriteLine(Row(movie.Title, movie.Year.ToString(), result.Score.ToString(), movie.Rating.ToString("F1"), movie.TmdbId.ToString(), tmdbUrl));
		}
		Console.WriteLine(Line());
		var mode = searchExtended ? "extended" : "title-only";
		Console.WriteLine($"{results.Count} result(s) for \"{searchTerm}\" (threshold: {searchThreshold}, mode: {mode}).");
		return;
	}

	if (migrateOnly)
	{
		await syncService.MigrateOnlyAsync();
		return;
	}

	await syncService.RunSyncAsync(tmdbEnrich, batchSize, outputPath);
}
catch (Exception ex)
{
	logger.LogError(ex, "An error occurred during execution");
	Environment.Exit(1);
}
finally
{
	Log.CloseAndFlush();
}

static void LoadEnvFile(string envPath)
{
	if (!File.Exists(envPath))
		return;

	var envLines = File.ReadAllLines(envPath);
	foreach (var line in envLines)
	{
		if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
			continue;

		var parts = line.Split('=', 2);
		if (parts.Length == 2)
		{
			Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim().Trim('\''));
		}
	}
}
