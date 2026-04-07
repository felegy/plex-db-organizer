using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PlexTmdbSync.Core;
using Serilog;
using Serilog.Events;
using System.CommandLine;
using Microsoft.OpenApi.Models;

LoadEnvFile(".env");

var services = new ServiceCollection();

Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Information()
	.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
	.Enrich.FromLogContext()
	.WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
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
var appDbService = serviceProvider.GetRequiredService<AppDatabaseService>();

var outputOption = new Option<string>("--output", () => "assets/csv/plex_movies.csv", "Output CSV path.");
var tmdbOption = new Option<string>("--tmdb", () => "true", "Enable TMDB enrichment (true|false).");
var batchOption = new Option<int>("--batch", () => 10, "Batch size for TMDB API calls.");
var searchOption = new Option<string?>("--search", "Search movies in CSV.");
var thresholdOption = new Option<int>("--threshold", () => 60, "Minimum fuzzy score for search.");
var searchExtendedOption = new Option<bool>("--search-extended", "Extend search to Summary, Genres, FilePath, and TmdbOverview.");
var syncOption = new Option<bool>("--sync", "Run full sync.");
var migrateOnlyOption = new Option<bool>("--migrate-only", "Run app database migrations only, then exit.");
var listMustDeleteOption = new Option<bool>("--list-must-delete", "List movies marked as MustDelete.");
var markMustDeleteOption = new Option<int?>("--mark-must-delete", "Mark one movie by id as MustDelete.")
{
	Arity = ArgumentArity.ExactlyOne
};

var apiUrlsOption = new Option<string?>("--urls", "ASPNETCORE_URLS override for the API server.");
var apiCommand = new Command("api", "Run API server from the CLI.");
apiCommand.AddOption(apiUrlsOption);
apiCommand.SetHandler(async context =>
{
	var urls = context.ParseResult.GetValueForOption(apiUrlsOption);
	
	var exitCode = await RunApiServerAsync(urls, logger);
	Environment.ExitCode = exitCode;
});

var rootCommand = new RootCommand("Plex TMDB Sync CLI");
rootCommand.AddOption(outputOption);
rootCommand.AddOption(tmdbOption);
rootCommand.AddOption(batchOption);
rootCommand.AddOption(searchOption);
rootCommand.AddOption(thresholdOption);
rootCommand.AddOption(searchExtendedOption);
rootCommand.AddOption(syncOption);
rootCommand.AddOption(migrateOnlyOption);
rootCommand.AddOption(listMustDeleteOption);
rootCommand.AddOption(markMustDeleteOption);
rootCommand.AddCommand(apiCommand);

rootCommand.AddValidator(result =>
{
	var batchSize = result.GetValueForOption(batchOption);
	if (batchSize <= 0)
	{
		result.ErrorMessage = "--batch must be greater than 0.";
		return;
	}

	var tmdbValue = result.GetValueForOption(tmdbOption);
	if (!bool.TryParse(tmdbValue, out _))
	{
		result.ErrorMessage = "--tmdb must be either true or false.";
		return;
	}

	var threshold = result.GetValueForOption(thresholdOption);
	if (threshold < 0 || threshold > 100)
	{
		result.ErrorMessage = "--threshold must be between 0 and 100.";
		return;
	}

	var markMustDeleteId = result.GetValueForOption(markMustDeleteOption);
	if (markMustDeleteId is <= 0)
	{
		result.ErrorMessage = "--mark-must-delete must be greater than 0.";
		return;
	}

	var explicitModeCount = 0;
	if (result.GetValueForOption(searchOption) is not null) explicitModeCount++;
	if (result.GetValueForOption(migrateOnlyOption)) explicitModeCount++;
	if (result.GetValueForOption(listMustDeleteOption)) explicitModeCount++;
	if (result.GetValueForOption(markMustDeleteOption).HasValue) explicitModeCount++;
	if (result.GetValueForOption(syncOption)) explicitModeCount++;

	if (explicitModeCount > 1)
	{
		result.ErrorMessage = "Use only one mode at a time: --sync, --search, --migrate-only, --list-must-delete, or --mark-must-delete.";
	}
});

rootCommand.SetHandler(async context =>
{
	var outputPath = context.ParseResult.GetValueForOption(outputOption) ?? "assets/csv/plex_movies.csv";
	var tmdbValue = context.ParseResult.GetValueForOption(tmdbOption) ?? "true";
	var tmdbEnrich = bool.Parse(tmdbValue);
	var batchSize = context.ParseResult.GetValueForOption(batchOption);
	var searchTerm = context.ParseResult.GetValueForOption(searchOption);
	var searchThreshold = context.ParseResult.GetValueForOption(thresholdOption);
	var searchExtended = context.ParseResult.GetValueForOption(searchExtendedOption);
	var sync = context.ParseResult.GetValueForOption(syncOption);
	var migrateOnly = context.ParseResult.GetValueForOption(migrateOnlyOption);
	var listMustDelete = context.ParseResult.GetValueForOption(listMustDeleteOption);
	var markMustDeleteId = context.ParseResult.GetValueForOption(markMustDeleteOption);

	var exitCode = await ExecuteAsync(
		outputPath,
		tmdbEnrich,
		batchSize,
		searchTerm,
		searchThreshold,
		searchExtended,
		sync,
		migrateOnly,
		listMustDelete,
		markMustDeleteId,
		logger,
		syncService,
		searchService,
		appDbService);

	Environment.ExitCode = exitCode;
});

try
{
	Environment.ExitCode = await rootCommand.InvokeAsync(args);
}
finally
{
	Log.CloseAndFlush();
}

static async Task<int> ExecuteAsync(
	string outputPath,
	bool tmdbEnrich,
	int batchSize,
	string? searchTerm,
	int searchThreshold,
	bool searchExtended,
	bool sync,
	bool migrateOnly,
	bool listMustDelete,
	int? markMustDeleteId,
	Microsoft.Extensions.Logging.ILogger logger,
	MovieSyncService syncService,
	MovieSearchService searchService,
	AppDatabaseService appDbService)
{
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
				return 0;
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
			return 0;
		}

		if (migrateOnly)
		{
			await syncService.MigrateOnlyAsync();
			return 0;
		}

		if (markMustDeleteId.HasValue)
		{
			await appDbService.InitializeAsync();
			var updated = await appDbService.SetMustDeleteAsync(markMustDeleteId.Value, true);
			if (!updated)
			{
				Console.WriteLine($"Movie not found for id {markMustDeleteId.Value}.");
				return 0;
			}

			Console.WriteLine($"Movie {markMustDeleteId.Value} marked as MustDelete.");
			return 0;
		}

		if (listMustDelete)
		{
			await appDbService.InitializeAsync();
			var mustDeleteMovies = await appDbService.GetMustDeleteMoviesAsync();

			if (mustDeleteMovies.Count == 0)
			{
				Console.WriteLine("No movies are currently marked as MustDelete.");
				return 0;
			}

			foreach (var movie in mustDeleteMovies)
			{
				Console.WriteLine($"{movie.Id}\t{movie.Title}\t({movie.Year})");
			}

			Console.WriteLine($"Total: {mustDeleteMovies.Count} movie(s) marked as MustDelete.");
			return 0;
		}

		if (sync)
		{
			await syncService.RunSyncAsync(tmdbEnrich, batchSize, outputPath);
			return 0;
		}

		await syncService.RunSyncAsync(tmdbEnrich, batchSize, outputPath);
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "An error occurred during execution");
		return 1;
	}
}

async Task<int> RunApiServerAsync(
	string? urls,
	Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		var resolvedUrls =
			string.IsNullOrWhiteSpace(urls)
				? (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5242")
				: urls;

		logger.LogInformation("Starting in-process API server from CLI. URLs: {Urls}", resolvedUrls);

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls(resolvedUrls);
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen(options =>
		{
			options.SwaggerDoc("v1", new OpenApiInfo
			{
				Title = "Plex TMDB Sync API",
				Version = "v1",
				Description = "REST API for migrating, syncing, exporting, and searching Plex movie metadata enriched with TMDB data."
			});
		});
		builder.Services.AddPlexTmdbCore();

		var app = builder.Build();
		MapApiEndpoints(app);

		await app.RunAsync();
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to run API server from CLI");
		return 1;
	}
}

void MapApiEndpoints(WebApplication app)
{
	app.UseSwagger();
	app.UseSwaggerUI(options =>
	{
		options.SwaggerEndpoint("/swagger/v1/swagger.json", "Plex TMDB Sync API v1");
		options.RoutePrefix = "swagger";
	});

	app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html"));

	app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
		.WithName("HealthCheck")
		.WithSummary("Returns API health status")
		.WithDescription("Simple health-check endpoint used to verify that the API is running.")
		.Produces(StatusCodes.Status200OK);

	app.MapPost("/migrate", async (MovieSyncService syncService) =>
	{
		await syncService.MigrateOnlyAsync();
		return Results.Ok(new { message = "Migrations completed" });
	})
		.WithName("RunMigrations")
		.WithSummary("Runs app database migrations")
		.WithDescription("Initializes the application database and applies any pending schema migrations.")
		.Produces(StatusCodes.Status200OK);

	app.MapPost("/sync", async (MovieSyncService syncService, SyncRequest request) =>
	{
		var outputPath = string.IsNullOrWhiteSpace(request.OutputPath)
			? "assets/csv/plex_movies.csv"
			: request.OutputPath;

		var movies = await syncService.RunSyncAsync(
			request.TmdbEnrich ?? true,
			request.BatchSize ?? 10,
			outputPath);

		return Results.Ok(new
		{
			message = "Sync completed",
			count = movies.Count,
			outputPath
		});
	})
		.WithName("RunSync")
		.WithSummary("Runs Plex to app-database sync")
		.WithDescription("Reads Plex metadata, optionally enriches movies from TMDB, stores them in the app database, and exports CSV output.")
		.Produces(StatusCodes.Status200OK);

	app.MapGet("/movies", async (AppDatabaseService appDbService) =>
	{
		await appDbService.InitializeAsync();
		var movies = await appDbService.GetMoviesAsync();
		return Results.Ok(movies);
	})
		.WithName("GetMovies")
		.WithSummary("Returns movies from the app database")
		.WithDescription("Reads all movies from the application database after ensuring the schema is initialized.")
		.Produces(StatusCodes.Status200OK);

	app.MapGet("/search", async (
		MovieSearchService searchService,
		string term,
		string? outputPath,
		int? threshold,
		bool? extended) =>
	{
		var csvPath = string.IsNullOrWhiteSpace(outputPath) ? "assets/csv/plex_movies.csv" : outputPath;
		var results = await searchService.SearchInCsvAsync(
			csvPath,
			term,
			threshold ?? 60,
			extended ?? false);

		return Results.Ok(results.Select(r => new
		{
			r.Score,
			Movie = r.Movie,
			TmdbUrl = r.Movie.TmdbId > 0 ? $"https://www.themoviedb.org/movie/{r.Movie.TmdbId}" : null
		}));
	})
		.WithName("SearchMovies")
		.WithSummary("Searches generated CSV content")
		.WithDescription("Performs fuzzy movie search against the exported CSV using title-only or extended search mode.")
		.Produces(StatusCodes.Status200OK);

	app.MapPost("/movies/{id:int}/must-delete", async (AppDatabaseService appDbService, int id) =>
	{
		if (id <= 0)
			return Results.BadRequest("Movie id must be greater than 0.");

		await appDbService.InitializeAsync();
		var updated = await appDbService.SetMustDeleteAsync(id, true);

		if (!updated)
			return Results.NotFound();

		return Results.Ok(new
		{
			message = "Movie marked as MustDelete",
			id,
			mustDelete = true
		});
	})
		.WithName("MarkMovieMustDelete")
		.WithSummary("Marks a movie for deletion")
		.WithDescription("Sets the MustDelete flag to true for the specified movie ID in the application database.")
		.Produces(StatusCodes.Status200OK)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status404NotFound);

	app.MapGet("/movies/must-delete", async (AppDatabaseService appDbService) =>
	{
		await appDbService.InitializeAsync();
		var movies = await appDbService.GetMustDeleteMoviesAsync();
		return Results.Ok(movies);
	})
		.WithName("GetMustDeleteMovies")
		.WithSummary("Returns movies marked for deletion")
		.WithDescription("Reads movies from the application database where MustDelete is true.")
		.Produces(StatusCodes.Status200OK);
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

public sealed record SyncRequest(bool? TmdbEnrich, int? BatchSize, string? OutputPath);
