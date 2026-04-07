using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;
using System.Text.Json;

namespace PlexTmdbSync.Core;

public class TmdbService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigService _config;
    private readonly ILogger<TmdbService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TmdbService(ConfigService config, ILogger<TmdbService> logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<int?> SearchMovieIdAsync(string title, int year)
    {
        try
        {
            _ = _config.RequireTmdbApiKey();
            var searchUrl = $"{_config.TmdbApiBaseUrl}/search/movie?query={Uri.EscapeDataString(title)}&year={year}";

            var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.Add("Authorization", $"Bearer {_config.RequireTmdbAccessToken()}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var searchResult = JsonSerializer.Deserialize<TmdbSearchResult>(content, _jsonOptions);

            if (searchResult?.Results.Count > 0)
            {
                var bestMatch = searchResult.Results[0];
                _logger.LogInformation("Found TMDB match for '{Title}' ({Year}): ID {Id}", title, year, bestMatch.Id);
                return bestMatch.Id;
            }

            _logger.LogWarning("No TMDB match found for '{Title}' ({Year})", title, year);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for '{Title}' on TMDB", title);
            return null;
        }
    }

    public async Task<TmdbMovieResponse?> GetMovieDetailsAsync(int tmdbId)
    {
        try
        {
            _ = _config.RequireTmdbApiKey();
            var detailUrl = $"{_config.TmdbApiBaseUrl}/movie/{tmdbId}";

            var request = new HttpRequestMessage(HttpMethod.Get, detailUrl);
            request.Headers.Add("Authorization", $"Bearer {_config.RequireTmdbAccessToken()}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TmdbMovieResponse>(content, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching movie details for ID {TmdbId}", tmdbId);
            return null;
        }
    }

    public async Task EnrichMovieWithTmdbDataAsync(PlexMovie movie)
    {
        if (string.IsNullOrWhiteSpace(movie.Title))
            return;

        if (movie.TmdbId == 0)
        {
            movie.TmdbId = await SearchMovieIdAsync(movie.Title, movie.Year) ?? 0;
        }

        if (movie.TmdbId > 0)
        {
            var details = await GetMovieDetailsAsync(movie.TmdbId);
            if (details != null)
            {
                movie.TmdbRating = details.VoteAverage;
                movie.TmdbOverview = details.Overview;
                if (!string.IsNullOrEmpty(details.PosterPath))
                {
                    movie.TmdbPosterUrl = $"https://image.tmdb.org/t/p/w500{details.PosterPath}";
                }
            }
        }

        await Task.Delay(500);
    }
}
