using Microsoft.Extensions.DependencyInjection;

namespace PlexTmdbSync.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlexTmdbCore(this IServiceCollection services)
    {
        services.AddSingleton<ConfigService>();
        services.AddSingleton<PlexDatabaseService>();
        services.AddSingleton<AppDatabaseService>();
        services.AddSingleton<TmdbService>();
        services.AddSingleton<ImdbService>();
        services.AddSingleton<CsvExportService>();
        services.AddSingleton<MovieSearchService>();
        services.AddSingleton<MovieSyncService>();
        return services;
    }
}
