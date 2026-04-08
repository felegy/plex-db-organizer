using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PlexTmdbSync.ApiHost;
using PlexTmdbSync.Core;
using PlexTmdbSync.Types;
using Serilog;
using Serilog.Events;
using System.CommandLine;
using System.Net.Http;
using System.Text;
using System.Text.Json;

ApiHostModule.LoadEnvFile(".env");

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
var omdbOption = new Option<string>("--omdb", () => "true", "Enable OMDb enrichment (true|false).");
var batchOption = new Option<int>("--batch", () => 10, "Batch size for external API calls.");
var searchOption = new Option<string?>("--search", "Search movies in CSV. (Prefer: client search --term)");
var thresholdOption = new Option<int>("--threshold", () => 60, "Minimum fuzzy score for search.");
var searchExtendedOption = new Option<bool>("--search-extended", "Extend search to Summary, Genres, FilePath, and TmdbOverview.");
var syncOption = new Option<bool>("--sync", "Run full sync. (Prefer: client sync)");
var migrateOnlyOption = new Option<bool>("--migrate-only", "Run app database migrations only, then exit. (Prefer: client migrate)");
var listMustDeleteOption = new Option<bool>("--list-must-delete", "List movies marked as MustDelete. (Prefer: client movies must-delete list)");
var markMustDeleteOption = new Option<int?>("--mark-must-delete", "Mark one movie by id as MustDelete. (Prefer: client movies must-delete mark --id)")
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

var clientApiUrlOption = new Option<string?>("--api-url", "Base URL for REST API client requests.");
var clientJsonOption = new Option<bool>("--json", "Output raw JSON instead of formatted table.");
var clientVerboseOption = new Option<bool>("--verbose", "Print HTTP request/response details to stderr.");
var clientCommand = new Command("client", "REST API client commands.");
clientCommand.AddOption(clientApiUrlOption);
clientCommand.AddOption(clientJsonOption);
clientCommand.AddOption(clientVerboseOption);

var clientHealthCommand = new Command("health", "Call API health endpoint.");
clientHealthCommand.SetHandler(async context =>
{
	var apiBaseUrl = ResolveClientApiUrl(context.ParseResult.GetValueForOption(clientApiUrlOption));
	var verbose = context.ParseResult.GetValueForOption(clientVerboseOption);
	var exitCode = await RunClientHealthAsync(apiBaseUrl, verbose, logger);
	Environment.ExitCode = exitCode;
});

var clientMigrateCommand = new Command("migrate", "Call API migrate endpoint.");
clientMigrateCommand.SetHandler(async context =>
{
	var apiBaseUrl = ResolveClientApiUrl(context.ParseResult.GetValueForOption(clientApiUrlOption));
	var verbose = context.ParseResult.GetValueForOption(clientVerboseOption);
	var exitCode = await RunClientMigrateAsync(apiBaseUrl, verbose, logger);
	Environment.ExitCode = exitCode;
});

var clientSyncTmdbEnrichOption = new Option<bool>("--tmdb-enrich", () => true, "Enable TMDB enrichment for sync.");
var clientSyncOmdbEnrichOption = new Option<bool>("--omdb-enrich", () => true, "Enable OMDb enrichment for sync.");
var clientSyncBatchSizeOption = new Option<int>("--batch-size", () => 10, "Batch size used by sync endpoint.");
var clientSyncOutputPathOption = new Option<string?>("--output-path", "CSV output path used by sync endpoint.");
var clientSyncCommand = new Command("sync", "Call API sync endpoint.");
clientSyncCommand.AddOption(clientSyncTmdbEnrichOption);
clientSyncCommand.AddOption(clientSyncOmdbEnrichOption);
clientSyncCommand.AddOption(clientSyncBatchSizeOption);
clientSyncCommand.AddOption(clientSyncOutputPathOption);
clientSyncCommand.AddValidator(result =>
{
	var batchSize = result.GetValueForOption(clientSyncBatchSizeOption);
	if (batchSize <= 0)
		result.ErrorMessage = "--batch-size must be greater than 0.";
});
clientSyncCommand.SetHandler(async context =>
{
	var apiBaseUrl = ResolveClientApiUrl(context.ParseResult.GetValueForOption(clientApiUrlOption));
	var tmdbEnrich = context.ParseResult.GetValueForOption(clientSyncTmdbEnrichOption);
	var omdbEnrich = context.ParseResult.GetValueForOption(clientSyncOmdbEnrichOption);
	var batchSize = context.ParseResult.GetValueForOption(clientSyncBatchSizeOption);
	var outputPath = context.ParseResult.GetValueForOption(clientSyncOutputPathOption);

	var verbose = context.ParseResult.GetValueForOption(clientVerboseOption);
	var exitCode = await RunClientSyncAsync(apiBaseUrl, tmdbEnrich, omdbEnrich, batchSize, outputPath, verbose, logger);
	Environment.ExitCode = exitCode;
});

var clientMoviesCommand = new Command("movies", "Call API movie endpoints.");

var clientMoviesListCommand = new Command("list", "Call GET /movies.");
clientMoviesListCommand.SetHandler(async context =>
{
	var apiBaseUrl = ResolveClientApiUrl(context.ParseResult.GetValueForOption(clientApiUrlOption));
	var verbose = context.ParseResult.GetValueForOption(clientVerboseOption);
	var exitCode = await RunClientMoviesListAsync(apiBaseUrl, verbose, logger);
	Environment.ExitCode = exitCode;
});

var clientMoviesMustDeleteCommand = new Command("must-delete", "Call API must-delete movie endpoints.");

var clientMoviesMustDeleteListCommand = new Command("list", "Call GET /movies/must-delete.");
clientMoviesMustDeleteListCommand.SetHandler(async context =>
{
	var apiBaseUrl = ResolveClientApiUrl(context.ParseResult.GetValueForOption(clientApiUrlOption));
	var verbose = context.ParseResult.GetValueForOption(clientVerboseOption);
	var exitCode = await RunClientMoviesMustDeleteListAsync(apiBaseUrl, verbose, logger);
	Environment.ExitCode = exitCode;
});

var clientMoviesMustDeleteMarkIdOption = new Option<int>("--id", "Movie id to mark as MustDelete.")
{
	IsRequired = true
};
var clientMoviesMustDeleteMarkCommand = new Command("mark", "Call POST /movies/{id}/must-delete.");
clientMoviesMustDeleteMarkCommand.AddOption(clientMoviesMustDeleteMarkIdOption);
clientMoviesMustDeleteMarkCommand.AddValidator(result =>
{
	var id = result.GetValueForOption(clientMoviesMustDeleteMarkIdOption);
	if (id <= 0)
		result.ErrorMessage = "--id must be greater than 0.";
});
clientMoviesMustDeleteMarkCommand.SetHandler(async context =>
{
	var apiBaseUrl = ResolveClientApiUrl(context.ParseResult.GetValueForOption(clientApiUrlOption));
	var id = context.ParseResult.GetValueForOption(clientMoviesMustDeleteMarkIdOption);
	var verbose = context.ParseResult.GetValueForOption(clientVerboseOption);
	var exitCode = await RunClientMoviesMustDeleteMarkAsync(apiBaseUrl, id, verbose, logger);
	Environment.ExitCode = exitCode;
});

clientMoviesMustDeleteCommand.AddCommand(clientMoviesMustDeleteListCommand);
clientMoviesMustDeleteCommand.AddCommand(clientMoviesMustDeleteMarkCommand);
clientMoviesCommand.AddCommand(clientMoviesListCommand);
clientMoviesCommand.AddCommand(clientMoviesMustDeleteCommand);

var clientSearchTermOption = new Option<string>("--term", "Search term.") { IsRequired = true };
var clientSearchOutputPathOption = new Option<string?>("--output-path", "CSV path to search in (default: assets/csv/plex_movies.csv).");
var clientSearchThresholdOption = new Option<int>("--threshold", () => 60, "Minimum fuzzy score (0–100).");
var clientSearchExtendedOption = new Option<bool>("--extended", "Search in Summary, Genres, FilePath, and TmdbOverview.");
var clientSearchCommand = new Command("search", "Call GET /search.");
clientSearchCommand.AddOption(clientSearchTermOption);
clientSearchCommand.AddOption(clientSearchOutputPathOption);
clientSearchCommand.AddOption(clientSearchThresholdOption);
clientSearchCommand.AddOption(clientSearchExtendedOption);
clientSearchCommand.AddValidator(result =>
{
	var threshold = result.GetValueForOption(clientSearchThresholdOption);
	if (threshold < 0 || threshold > 100)
		result.ErrorMessage = "--threshold must be between 0 and 100.";
});
clientSearchCommand.SetHandler(async context =>
{
	var apiBaseUrl = ResolveClientApiUrl(context.ParseResult.GetValueForOption(clientApiUrlOption));
	var term = context.ParseResult.GetValueForOption(clientSearchTermOption)!;
	var outputPath = context.ParseResult.GetValueForOption(clientSearchOutputPathOption);
	var threshold = context.ParseResult.GetValueForOption(clientSearchThresholdOption);
	var extended = context.ParseResult.GetValueForOption(clientSearchExtendedOption);
	var json = context.ParseResult.GetValueForOption(clientJsonOption);

	var verbose = context.ParseResult.GetValueForOption(clientVerboseOption);
	var exitCode = await RunClientSearchAsync(apiBaseUrl, term, outputPath, threshold, extended, json, verbose, logger);
	Environment.ExitCode = exitCode;
});

clientCommand.AddCommand(clientHealthCommand);
clientCommand.AddCommand(clientMigrateCommand);
clientCommand.AddCommand(clientSyncCommand);
clientCommand.AddCommand(clientMoviesCommand);
clientCommand.AddCommand(clientSearchCommand);

var rootCommand = new RootCommand("Plex TMDB Sync CLI");
rootCommand.AddOption(outputOption);
rootCommand.AddOption(tmdbOption);
rootCommand.AddOption(omdbOption);
rootCommand.AddOption(batchOption);
rootCommand.AddOption(searchOption);
rootCommand.AddOption(thresholdOption);
rootCommand.AddOption(searchExtendedOption);
rootCommand.AddOption(syncOption);
rootCommand.AddOption(migrateOnlyOption);
rootCommand.AddOption(listMustDeleteOption);
rootCommand.AddOption(markMustDeleteOption);
rootCommand.AddCommand(apiCommand);
rootCommand.AddCommand(clientCommand);

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

	var omdbValue = result.GetValueForOption(omdbOption);
	if (!bool.TryParse(omdbValue, out _))
	{
		result.ErrorMessage = "--omdb must be either true or false.";
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
	var omdbValue = context.ParseResult.GetValueForOption(omdbOption) ?? "true";
	var tmdbEnrich = bool.Parse(tmdbValue);
	var omdbEnrich = bool.Parse(omdbValue);
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
		omdbEnrich,
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
	bool omdbEnrich,
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
			await syncService.RunSyncAsync(tmdbEnrich, omdbEnrich, batchSize, outputPath);
			return 0;
		}

		await syncService.RunSyncAsync(tmdbEnrich, omdbEnrich, batchSize, outputPath);
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
		var allowedOrigins = ApiHostModule.GetConfiguredWebClientOrigins();
		ApiHostModule.ConfigureApiServices(builder, allowedOrigins, enableCors: true);

		var app = builder.Build();
		ApiHostModule.ConfigureApiPipeline(app, enableCors: true, serveWebClient: true);
		ApiHostModule.MapApiEndpoints(app);

		await app.RunAsync();
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to run API server from CLI");
		return 1;
	}
}

static string ResolveClientApiUrl(string? apiUrl)
{
	if (!string.IsNullOrWhiteSpace(apiUrl))
		return EnsureApiBasePath(apiUrl.Trim());

	var envUrl = Environment.GetEnvironmentVariable("API_BASE_URL")
		?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

	if (string.IsNullOrWhiteSpace(envUrl))
		return "http://localhost:5242/api";

	var firstUrl = envUrl
		.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
		.FirstOrDefault();

	if (string.IsNullOrWhiteSpace(firstUrl))
		return "http://localhost:5242/api";

	firstUrl = firstUrl.Replace("://+:", "://localhost:").Replace("://*:", "://localhost:");
	return EnsureApiBasePath(firstUrl.Trim());
}

static string EnsureApiBasePath(string url)
{
	var normalized = url.TrimEnd('/');
	if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
		return normalized;

	var authority = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
	var path = uri.AbsolutePath.TrimEnd('/');
	if (string.IsNullOrWhiteSpace(path))
		return $"{authority}/api";

	return path.Equals("/api", StringComparison.OrdinalIgnoreCase)
		? $"{authority}/api"
		: normalized;
}

static async Task<int> RunClientHealthAsync(string apiBaseUrl, bool verbose, Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		using var httpClient = new HttpClient
		{
			BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/"),
			Timeout = TimeSpan.FromSeconds(30)
		};

		if (verbose) Console.Error.WriteLine($"> GET {apiBaseUrl.TrimEnd('/')}/health");
		using var response = await httpClient.GetAsync("health");
		var responseBody = await response.Content.ReadAsStringAsync();
		if (verbose) Console.Error.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase}");

		if (!response.IsSuccessStatusCode)
		{
			PrintHttpError(response.StatusCode, response.ReasonPhrase, responseBody);
			return 1;
		}

		Console.WriteLine(responseBody);
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to call API health endpoint at {ApiBaseUrl}", apiBaseUrl);
		return 1;
	}
}

static async Task<int> RunClientMigrateAsync(string apiBaseUrl, bool verbose, Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		using var httpClient = new HttpClient
		{
			BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/"),
			Timeout = TimeSpan.FromSeconds(30)
		};

		if (verbose) Console.Error.WriteLine($"> POST {apiBaseUrl.TrimEnd('/')}/migrate");
		using var response = await httpClient.PostAsync("migrate", content: null);
		var responseBody = await response.Content.ReadAsStringAsync();
		if (verbose) Console.Error.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase}");

		if (!response.IsSuccessStatusCode)
		{
			PrintHttpError(response.StatusCode, response.ReasonPhrase, responseBody);
			return 1;
		}

		Console.WriteLine(string.IsNullOrWhiteSpace(responseBody) ? "Migrate endpoint completed." : responseBody);
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to call API migrate endpoint at {ApiBaseUrl}", apiBaseUrl);
		return 1;
	}
}

static async Task<int> RunClientSyncAsync(
	string apiBaseUrl,
	bool tmdbEnrich,
	bool omdbEnrich,
	int batchSize,
	string? outputPath,
	bool verbose,
	Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		using var httpClient = new HttpClient
		{
			BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/"),
			Timeout = TimeSpan.FromMinutes(10)
		};

		var payload = new
		{
			tmdbEnrich,
			omdbEnrich,
			batchSize,
			outputPath
		};

		using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		if (verbose) Console.Error.WriteLine($"> POST {apiBaseUrl.TrimEnd('/')}/sync");
		using var response = await httpClient.PostAsync("sync", content);
		var responseBody = await response.Content.ReadAsStringAsync();
		if (verbose) Console.Error.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase}");

		if (!response.IsSuccessStatusCode)
		{
			PrintHttpError(response.StatusCode, response.ReasonPhrase, responseBody);
			return 1;
		}

		Console.WriteLine(responseBody);
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to call API sync endpoint at {ApiBaseUrl}", apiBaseUrl);
		return 1;
	}
}

static async Task<int> RunClientMoviesListAsync(string apiBaseUrl, bool verbose, Microsoft.Extensions.Logging.ILogger logger)
{
	return await RunClientGetAsync(apiBaseUrl, "movies", verbose, logger);
}

static async Task<int> RunClientMoviesMustDeleteListAsync(string apiBaseUrl, bool verbose, Microsoft.Extensions.Logging.ILogger logger)
{
	return await RunClientGetAsync(apiBaseUrl, "movies/must-delete", verbose, logger);
}

static async Task<int> RunClientMoviesMustDeleteMarkAsync(string apiBaseUrl, int id, bool verbose, Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		using var httpClient = new HttpClient
		{
			BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/"),
			Timeout = TimeSpan.FromSeconds(30)
		};

		if (verbose) Console.Error.WriteLine($"> POST {apiBaseUrl.TrimEnd('/')}/movies/{id}/must-delete");
		using var response = await httpClient.PostAsync($"movies/{id}/must-delete", content: null);
		var responseBody = await response.Content.ReadAsStringAsync();
		if (verbose) Console.Error.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase}");

		if (!response.IsSuccessStatusCode)
		{
			PrintHttpError(response.StatusCode, response.ReasonPhrase, responseBody);
			return 1;
		}

		Console.WriteLine(responseBody);
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to call API must-delete mark endpoint at {ApiBaseUrl} for id {MovieId}", apiBaseUrl, id);
		return 1;
	}
}

static async Task<int> RunClientSearchAsync(
	string apiBaseUrl,
	string term,
	string? outputPath,
	int threshold,
	bool extended,
	bool json,
	bool verbose,
	Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		using var httpClient = new HttpClient
		{
			BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/"),
			Timeout = TimeSpan.FromSeconds(30)
		};

		var queryParams = new List<string>
		{
			$"term={Uri.EscapeDataString(term)}",
			$"threshold={threshold}",
			$"extended={extended.ToString().ToLowerInvariant()}"
		};
		if (!string.IsNullOrWhiteSpace(outputPath))
			queryParams.Add($"outputPath={Uri.EscapeDataString(outputPath)}");

		var searchRelativeUrl = $"search?{string.Join('&', queryParams)}";
		if (verbose) Console.Error.WriteLine($"> GET {apiBaseUrl.TrimEnd('/')}/{searchRelativeUrl}");
		using var response = await httpClient.GetAsync(searchRelativeUrl);
		var responseBody = await response.Content.ReadAsStringAsync();
		if (verbose) Console.Error.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase}");

		if (!response.IsSuccessStatusCode)
		{
			PrintHttpError(response.StatusCode, response.ReasonPhrase, responseBody);
			return 1;
		}

		if (json)
		{
			Console.WriteLine(responseBody);
			return 0;
		}

		var results = JsonSerializer.Deserialize<JsonElement[]>(responseBody) ?? [];

		if (results.Length == 0)
		{
			Console.WriteLine($"No movies found matching \"{term}\" (threshold: {threshold}).");
			Console.WriteLine("Tip: lower the threshold with --threshold 40");
			return 0;
		}

		var rows = results.Select(r => new
		{
			Title   = r.GetProperty("movie").TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
			Year    = r.GetProperty("movie").TryGetProperty("year", out var y) ? y.GetInt32() : 0,
			Score   = r.TryGetProperty("score", out var s) ? s.GetInt32() : 0,
			Rating  = r.GetProperty("movie").TryGetProperty("rating", out var rt) ? rt.GetDouble() : 0.0,
			TmdbId  = r.GetProperty("movie").TryGetProperty("tmdbId", out var id) ? id.GetInt32() : 0,
			TmdbUrl = r.TryGetProperty("tmdbUrl", out var u) && u.ValueKind != JsonValueKind.Null ? u.GetString() ?? string.Empty : string.Empty
		}).ToList();

		int titleW  = Math.Max(rows.Max(x => x.Title.Length), 5);
		int yearW   = 4;
		int scoreW  = 5;
		int ratingW = 6;
		int tmdbW   = Math.Max(rows.Max(x => x.TmdbId.ToString().Length), 6);
		int urlW    = 42;

		string Line() => "+" + new string('-', titleW + 2) + "+" + new string('-', yearW + 2) + "+" + new string('-', scoreW + 2) + "+" + new string('-', ratingW + 2) + "+" + new string('-', tmdbW + 2) + "+" + new string('-', urlW + 2) + "+";
		string Row(string t2, string y2, string s2, string r2, string tmdb2, string url2) =>
			$"| {t2.PadRight(titleW)} | {y2.PadRight(yearW)} | {s2.PadRight(scoreW)} | {r2.PadRight(ratingW)} | {tmdb2.PadRight(tmdbW)} | {url2.PadLeft(Math.Min(url2.Length, urlW)).PadRight(urlW)} |";

		Console.WriteLine(Line());
		Console.WriteLine(Row("Title", "Year", "Score", "Rating", "TmdbId", "TmdbUrl"));
		Console.WriteLine(Line());
		foreach (var row in rows)
		{
			var url = row.TmdbUrl.Length > urlW ? ".." + row.TmdbUrl[^(urlW - 2)..] : row.TmdbUrl;
			Console.WriteLine(Row(row.Title, row.Year.ToString(), row.Score.ToString(), row.Rating.ToString("F1"), row.TmdbId.ToString(), url));
		}
		Console.WriteLine(Line());
		var mode = extended ? "extended" : "title-only";
		Console.WriteLine($"{results.Length} result(s) for \"{term}\" (threshold: {threshold}, mode: {mode}).");
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to call API search endpoint at {ApiBaseUrl}", apiBaseUrl);
		return 1;
	}
}

static async Task<int> RunClientGetAsync(string apiBaseUrl, string relativePath, bool verbose, Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		using var httpClient = new HttpClient
		{
			BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/"),
			Timeout = TimeSpan.FromSeconds(30)
		};

		if (verbose) Console.Error.WriteLine($"> GET {apiBaseUrl.TrimEnd('/')}/{relativePath}");
		using var response = await httpClient.GetAsync(relativePath);
		var responseBody = await response.Content.ReadAsStringAsync();
		if (verbose) Console.Error.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase}");

		if (!response.IsSuccessStatusCode)
		{
			PrintHttpError(response.StatusCode, response.ReasonPhrase, responseBody);
			return 1;
		}

		Console.WriteLine(responseBody);
		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to call API endpoint {RelativePath} at {ApiBaseUrl}", relativePath, apiBaseUrl);
		return 1;
	}
}

static void PrintHttpError(System.Net.HttpStatusCode statusCode, string? reasonPhrase, string? responseBody)
{
	Console.WriteLine($"Request failed: {(int)statusCode} {reasonPhrase}");
	if (string.IsNullOrWhiteSpace(responseBody))
		return;

	try
	{
		var problem = JsonSerializer.Deserialize<ApiProblemDetails>(responseBody, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		});

		if (problem is null || string.IsNullOrWhiteSpace(problem.Title))
		{
			Console.WriteLine(responseBody);
			return;
		}

		Console.WriteLine($"Title: {problem.Title}");
		Console.WriteLine($"Detail: {problem.Detail}");
		Console.WriteLine($"TraceId: {problem.TraceId}");

		if (problem.Errors is not null)
		{
			foreach (var entry in problem.Errors)
			{
				foreach (var message in entry.Value)
				{
					Console.WriteLine($"- {entry.Key}: {message}");
				}
			}
		}
	}
	catch
	{
		Console.WriteLine(responseBody);
	}
}

