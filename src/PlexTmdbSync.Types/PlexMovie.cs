namespace PlexTmdbSync.Types;

public class PlexMovie
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? FilePath { get; set; }
    public int Year { get; set; }
    public string? Summary { get; set; }
    public double Rating { get; set; }
    public int Duration { get; set; }
    public string? Genres { get; set; }
    public int TmdbId { get; set; }
    public string? TmdbPosterUrl { get; set; }
    public double? TmdbRating { get; set; }
    public string? TmdbOverview { get; set; }
    public bool MustDelete { get; set; }
}
