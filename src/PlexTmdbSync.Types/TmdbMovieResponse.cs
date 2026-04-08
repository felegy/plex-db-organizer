using System.Text.Json.Serialization;

namespace PlexTmdbSync.Types;

public class TmdbMovieResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }
}

public class TmdbSearchResult
{
    [JsonPropertyName("results")]
    public List<TmdbMovieResponse> Results { get; set; } = new();
}
