namespace PlexTmdbSync.Types;

public class MovieSearchResult
{
    public PlexMovie Movie { get; set; } = new();
    public int Score { get; set; }
}
