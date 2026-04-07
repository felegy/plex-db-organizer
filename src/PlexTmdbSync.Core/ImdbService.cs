using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace PlexTmdbSync.Core;

public class ImdbService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigService _config;
    private readonly ILogger<ImdbService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ConcurrentDictionary<string, ImdbMovieResponse> _rapidApiDetailsCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxRetryBackoffSeconds = 30;

    public ImdbService(ConfigService config, ILogger<ImdbService> logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<string?> SearchMovieIdAsync(string title, int year)
    {
        if (_config.UseRapidApiForImdb())
            return await SearchMovieIdViaRapidApiAsync(title, year);

        try
        {
            var searchUrl = BuildSearchUrl(title, year);
            var response = await SendImdbRequestAsync(searchUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var searchResult = JsonSerializer.Deserialize<ImdbMovieResponse>(content, _jsonOptions);
            var imdbId = searchResult?.ImdbId;

            if (string.Equals(searchResult?.Response, "True", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(imdbId))
            {
                _logger.LogInformation("Found IMDB match for '{Title}' ({Year}): {ImdbId}", title, year, imdbId);
                return imdbId;
            }

            _logger.LogWarning("No IMDB match found for '{Title}' ({Year}): {Error}", title, year, searchResult?.Error ?? "Unknown error");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for '{Title}' on IMDB", title);
            return null;
        }
    }

    public async Task<ImdbMovieResponse?> GetMovieDetailsAsync(string imdbId)
    {
        if (_config.UseRapidApiForImdb())
        {
            if (_rapidApiDetailsCache.TryGetValue(imdbId, out var cached))
                return cached;

            _logger.LogDebug("RapidAPI details were not cached for IMDB id {ImdbId}", imdbId);
            return null;
        }

        try
        {
            var detailUrl = BuildDetailsUrl(imdbId);
            var response = await SendImdbRequestAsync(detailUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ImdbMovieResponse>(content, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching IMDB details for ID {ImdbId}", imdbId);
            return null;
        }
    }

    public async Task EnrichMovieWithImdbDataAsync(PlexMovie movie)
    {
        if (string.IsNullOrWhiteSpace(movie.Title))
            return;

        if (string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            movie.ImdbId = await SearchMovieIdAsync(movie.Title, movie.Year);
        }

        if (!string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            var details = await GetMovieDetailsAsync(movie.ImdbId);
            if (details != null && string.Equals(details.Response, "True", StringComparison.OrdinalIgnoreCase))
            {
                movie.ImdbId = details.ImdbId ?? movie.ImdbId;
                movie.ImdbRating = ParseNullableDouble(details.ImdbRating);
                movie.ImdbVotes = ParseNullableInt(details.ImdbVotes);

                if (!string.IsNullOrWhiteSpace(movie.ImdbId))
                    movie.ImdbUrl = $"https://www.imdb.com/title/{movie.ImdbId}/";
            }
        }

        await Task.Delay(500);
    }

    private string BuildSearchUrl(string title, int year)
    {
        var query = $"t={Uri.EscapeDataString(title)}&y={year}";
        if (!_config.UseRapidApiForImdb())
        {
            var apiKey = _config.RequireImdbApiKey();
            query += $"&apikey={Uri.EscapeDataString(apiKey)}";
        }

        return $"{_config.ImdbApiBaseUrl}/?{query}";
    }

    private string BuildRapidApiSearchUrl(string title, int year)
    {
        var baseUrl = _config.ImdbApiBaseUrl;
        if (!baseUrl.Contains("/api/imdb", StringComparison.OrdinalIgnoreCase))
            baseUrl = $"{baseUrl}/api/imdb";

        return $"{baseUrl}/search?type=movie&title={Uri.EscapeDataString(title)}&year={year}";
    }

    private string BuildDetailsUrl(string imdbId)
    {
        var query = $"i={Uri.EscapeDataString(imdbId)}";
        if (!_config.UseRapidApiForImdb())
        {
            var apiKey = _config.RequireImdbApiKey();
            query += $"&apikey={Uri.EscapeDataString(apiKey)}";
        }

        return $"{_config.ImdbApiBaseUrl}/?{query}";
    }

    private async Task<string?> SearchMovieIdViaRapidApiAsync(string title, int year)
    {
        try
        {
            var searchUrl = BuildRapidApiSearchUrl(title, year);
            var response = await SendImdbRequestAsync(searchUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(content);

            if (!document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("RapidAPI IMDB search returned unexpected response for '{Title}' ({Year})", title, year);
                return null;
            }

            var best = SelectBestRapidApiMovie(results, title, year);
            if (best.Id is null)
            {
                _logger.LogWarning("No IMDB match found for '{Title}' ({Year}) via RapidAPI", title, year);
                return null;
            }

            var mapped = new ImdbMovieResponse
            {
                ImdbId = best.Id,
                ImdbRating = best.AverageRating,
                ImdbVotes = best.NumVotes,
                Response = "True"
            };
            _rapidApiDetailsCache[best.Id] = mapped;

            _logger.LogInformation("Found IMDB match for '{Title}' ({Year}) via RapidAPI: {ImdbId}", title, year, best.Id);
            return best.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for '{Title}' on IMDB via RapidAPI", title);
            return null;
        }
    }

    private static (string? Id, string? AverageRating, string? NumVotes) SelectBestRapidApiMovie(JsonElement results, string title, int year)
    {
        var normalizedTarget = NormalizeTitle(title);
        var bestScore = int.MinValue;
        string? bestId = null;
        string? bestAverageRating = null;
        string? bestNumVotes = null;

        foreach (var item in results.EnumerateArray())
        {
            var type = ReadString(item, "type");
            if (!string.Equals(type, "movie", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var primaryTitle = ReadString(item, "primaryTitle");
            var originalTitle = ReadString(item, "originalTitle");
            var startYear = ReadInt(item, "startYear");

            var score = 0;
            var normalizedPrimary = NormalizeTitle(primaryTitle);
            var normalizedOriginal = NormalizeTitle(originalTitle);

            if (normalizedPrimary == normalizedTarget || normalizedOriginal == normalizedTarget)
                score += 100;
            else if (normalizedPrimary.Contains(normalizedTarget, StringComparison.Ordinal) || normalizedOriginal.Contains(normalizedTarget, StringComparison.Ordinal))
                score += 40;

            if (startYear == year)
                score += 30;
            else if (startYear.HasValue && Math.Abs(startYear.Value - year) <= 1)
                score += 10;

            var numVotes = ReadRawNumberAsString(item, "numVotes");
            var votes = 0;
            if (int.TryParse(numVotes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVotes))
                votes = parsedVotes;
            score += Math.Min(20, votes / 50000);

            if (score <= bestScore)
                continue;

            bestScore = score;
            bestId = id;
            bestAverageRating = ReadRawNumberAsString(item, "averageRating");
            bestNumVotes = numVotes;
        }

        return (bestId, bestAverageRating, bestNumVotes);
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            return intValue;

        return null;
    }

    private static string? ReadRawNumberAsString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;

        return value.GetRawText();
    }

    private async Task<HttpResponseMessage> SendImdbRequestAsync(string requestUrl)
    {
        for (var attempt = 1; attempt <= _config.ImdbMaxRetryAttempts; attempt++)
        {
            using var request = BuildRequest(requestUrl);
            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == _config.ImdbMaxRetryAttempts)
                return response;

            var delay = GetRetryDelay(response, attempt);
            _logger.LogWarning(
                "IMDB API rate limit reached (429). Waiting {DelayMs} ms before retry {Attempt}/{MaxAttempts}",
                (int)delay.TotalMilliseconds,
                attempt + 1,
                _config.ImdbMaxRetryAttempts);

            response.Dispose();
            await Task.Delay(delay);
        }

        throw new InvalidOperationException("IMDB request retry loop exited unexpectedly");
    }

    private HttpRequestMessage BuildRequest(string requestUrl)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        if (!_config.UseRapidApiForImdb())
            return request;

        var rapidApiKey = _config.RequireImdbApiKey();
        request.Headers.TryAddWithoutValidation("x-rapidapi-key", rapidApiKey);

        var rapidApiHost = _config.GetRapidApiHost();
        if (!string.IsNullOrWhiteSpace(rapidApiHost))
            request.Headers.TryAddWithoutValidation("x-rapidapi-host", rapidApiHost);

        return request;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            return delta;

        if (response.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
        {
            var dateDelay = retryAt - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
                return dateDelay;
        }

        var seconds = Math.Min(MaxRetryBackoffSeconds, (int)Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(seconds);
    }

    private static double? ParseNullableDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "N/A")
            return null;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "N/A")
            return null;

        var normalized = value.Replace(",", string.Empty, StringComparison.Ordinal);
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }
}
