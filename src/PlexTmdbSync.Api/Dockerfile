FROM node:24-alpine AS webbuild
WORKDIR /src

COPY package.json package-lock.json ./
COPY web ./web
RUN npm ci
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY plex-db.sln ./
COPY src/PlexTmdbSync.Types/PlexTmdbSync.Types.csproj src/PlexTmdbSync.Types/
COPY src/PlexTmdbSync.Core/PlexTmdbSync.Core.csproj src/PlexTmdbSync.Core/
COPY src/PlexTmdbSync.ApiHost/PlexTmdbSync.ApiHost.csproj src/PlexTmdbSync.ApiHost/
COPY src/PlexTmdbSync.Api/PlexTmdbSync.Api.csproj src/PlexTmdbSync.Api/

RUN dotnet restore src/PlexTmdbSync.Api/PlexTmdbSync.Api.csproj

COPY . .
RUN dotnet publish src/PlexTmdbSync.Api/PlexTmdbSync.Api.csproj -c Release -o /app/publish /p:UseAppHost=false
RUN mkdir -p /app/publish/wwwroot/app
COPY --from=webbuild /src/dist/. /app/publish/wwwroot/app/

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "PlexTmdbSync.Api.dll"]
