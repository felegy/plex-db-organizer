FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY plex-db.sln ./
COPY src/PlexTmdbSync.Types/PlexTmdbSync.Types.csproj src/PlexTmdbSync.Types/
COPY src/PlexTmdbSync.Core/PlexTmdbSync.Core.csproj src/PlexTmdbSync.Core/
COPY src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj src/PlexTmdbSync.Cli/

RUN dotnet restore src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj

COPY . .
RUN dotnet publish src/PlexTmdbSync.Cli/PlexTmdbSync.Cli.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PlexTmdbSync.Cli.dll"]
