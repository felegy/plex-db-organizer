using CsvHelper;
using CsvHelper.Configuration;
using FuzzySharp;
using PlexTmdbSync.Types;
using System.Globalization;

namespace PlexTmdbSync.Core;

public class MovieSearchService
{
    public async Task<List<MovieSearchResult>> SearchInCsvAsync(string csvPath, string searchTerm, int searchThreshold, bool searchExtended)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"CSV file not found: {csvPath}", csvPath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };
        using var reader = new StreamReader(csvPath);
        using var csv = new CsvReader(reader, config);

        static string Normalize(string s) => new string(
            s.Normalize(System.Text.NormalizationForm.FormD)
             .Where(c => CharUnicodeInfo.GetUnicodeCategory(c)
                         != UnicodeCategory.NonSpacingMark)
             .ToArray()
        ).ToLowerInvariant();

        static string[] Tokens(string s) => s
            .Split(new[] { ' ', '-', '_', ':', '.', ',', ';', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

        var normQuery = Normalize(searchTerm);
        var queryTokens = Tokens(normQuery);
        var isMultiWordQuery = queryTokens.Length > 1;

        var scored = csv.GetRecords<PlexMovie>()
            .Select(m =>
            {
                var fields = new List<string>
                {
                    m.Title,
                    m.OriginalTitle ?? string.Empty
                };

                if (searchExtended)
                {
                    fields.Add(m.Summary ?? string.Empty);
                    fields.Add(m.Genres ?? string.Empty);
                    fields.Add(m.FilePath ?? string.Empty);
                    fields.Add(m.TmdbOverview ?? string.Empty);
                }

                var normalizedFields = fields
                    .Select(Normalize)
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToList();

                int weightedScore = normalizedFields
                    .Select(f => Fuzz.WeightedRatio(normQuery, f))
                    .DefaultIfEmpty(0)
                    .Max();

                int tokenSetScore = normalizedFields
                    .Select(f => Fuzz.TokenSetRatio(normQuery, f))
                    .DefaultIfEmpty(0)
                    .Max();

                bool containsWholeQuery = normalizedFields.Any(f => f.Contains(normQuery, StringComparison.Ordinal));

                bool hasTokenOverlap = normalizedFields.Any(f =>
                {
                    var fieldTokens = Tokens(f).ToHashSet(StringComparer.Ordinal);
                    return queryTokens.Any(t => t.Length >= 3 && fieldTokens.Contains(t));
                });

                int score = Math.Max(weightedScore, tokenSetScore);

                bool include = containsWholeQuery;
                if (!include)
                {
                    include = isMultiWordQuery
                        ? (tokenSetScore >= searchThreshold && hasTokenOverlap)
                        : weightedScore >= searchThreshold;
                }

                return new { Movie = m, Score = score, Include = include };
            })
            .Where(x => x.Include)
            .OrderByDescending(x => x.Score)
            .Select(x => new MovieSearchResult { Movie = x.Movie, Score = x.Score })
            .ToList();

        return await Task.FromResult(scored);
    }
}
