using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;
using System.Globalization;
using System.Text.Json;

namespace PlexTmdbSync.Core;

public class OmdbService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigService _config;
    private readonly ILogger<OmdbService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OmdbService(ConfigService config, ILogger<OmdbService> logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task EnrichMovieWithOmdbDataAsync(PlexMovie movie)
    {
        if (string.IsNullOrWhiteSpace(movie.Title) && string.IsNullOrWhiteSpace(movie.OriginalTitle))
            return;

        var details = await GetMovieByTitleAsync(movie.Title, movie.OriginalTitle, movie.Year);
        if (details is null || !string.Equals(details.Response, "True", StringComparison.OrdinalIgnoreCase))
            return;

        movie.ImdbId = details.ImdbId;
        movie.ImdbRating = ParseNullableDouble(details.ImdbRating);
        movie.ImdbVotes = ParseNullableInt(details.ImdbVotes);

        if (!string.IsNullOrWhiteSpace(movie.ImdbId))
            movie.ImdbUrl = $"https://www.imdb.com/title/{movie.ImdbId}/";

        await Task.Delay(250);
    }

    private async Task<OmdbMovieResponse?> GetMovieByTitleAsync(string? title, string? originalTitle, int year)
    {
        var rawCandidates = new[] { originalTitle, title }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();

        // Build candidate list: each raw title + its normalized variant
        var candidateTitles = rawCandidates
            .SelectMany(t => new[] { t, NormalizeTitle(t) })
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        // When year is unknown, also try without the year constraint
        var yearsToTry = year > 0
            ? new[] { year, 0 }
            : new[] { 0 };

        foreach (var candidateTitle in candidateTitles)
        {
            foreach (var yearToTry in yearsToTry)
            {
                try
                {
                    var requestUrl = BuildTitleRequestUrl(candidateTitle, yearToTry);
                    var content = await _httpClient.GetStringAsync(requestUrl);
                    var result = JsonSerializer.Deserialize<OmdbMovieResponse>(content, _jsonOptions);

                    if (result is not null && string.Equals(result.Response, "True", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation(
                            "Found OMDb match for '{CandidateTitle}' ({Year}): {ImdbId}",
                            candidateTitle, yearToTry > 0 ? yearToTry.ToString() : "any", result.ImdbId);
                        return result;
                    }

                    _logger.LogDebug(
                        "No OMDb match for '{CandidateTitle}' ({Year}): {Error}",
                        candidateTitle, yearToTry, result?.Error ?? "Unknown error");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error searching for '{CandidateTitle}' on OMDb", candidateTitle);
                }
            }
        }

        _logger.LogWarning(
            "No OMDb match found for '{Title}' / '{OriginalTitle}' ({Year})",
            title ?? string.Empty,
            originalTitle ?? string.Empty,
            year);
        return null;
    }

    /// <summary>
    /// Returns a normalized variant of a title for use as a fallback OMDb search candidate.
    /// Strips trailing period-suffixed ordinal numbers (e.g. "Rocky IV." → "Rocky IV"),
    /// trailing roman/arabic numerals with period (e.g. "2." → "2"), and subtitle after " - ".
    /// Returns null if the result equals the original after trimming.
    /// </summary>
    private static string? NormalizeTitle(string title)
    {
        var normalized = title.TrimEnd().TrimEnd('.');

        // e.g. "Sherlock Holmes 2." → "Sherlock Holmes 2"
        if (string.Equals(normalized, title.TrimEnd(), StringComparison.Ordinal))
            normalized = null;

        // Also try stripping subtitle: "Creed - Apollo fia" → "Creed"
        var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0)
        {
            var withoutSubtitle = title[..dashIndex].Trim();
            if (!string.Equals(withoutSubtitle, normalized, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(withoutSubtitle, title, StringComparison.OrdinalIgnoreCase))
            {
                return normalized ?? withoutSubtitle;
            }
        }

        return normalized;
    }

    private string BuildTitleRequestUrl(string title, int year)
    {
        var apiKey = Uri.EscapeDataString(_config.RequireOmdbApiKey());
        var escapedTitle = Uri.EscapeDataString(title);
        var yearParam = year > 0 ? $"&y={year}" : string.Empty;
        return $"{_config.OmdbApiBaseUrl}?apikey={apiKey}&t={escapedTitle}{yearParam}";
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