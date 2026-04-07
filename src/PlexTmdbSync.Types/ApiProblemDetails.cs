namespace PlexTmdbSync.Types;

public sealed record ApiProblemDetails(
    string Title,
    int Status,
    string Detail,
    string Instance,
    string TraceId,
    Dictionary<string, string[]>? Errors = null
);
