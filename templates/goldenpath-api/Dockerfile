# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------------------------------------
# Build stage: the full SDK lives here and only here.
# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

# Restore in its own layer, reused until a project file or a package version changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Company.Service.Api/Company.Service.Api.csproj src/Company.Service.Api/
RUN dotnet restore src/Company.Service.Api/Company.Service.Api.csproj

COPY src/ src/
RUN dotnet publish src/Company.Service.Api/Company.Service.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    -p:UseAppHost=false

# ---------------------------------------------------------------------------------------------------------
# Runtime stage: Ubuntu chiseled. No shell, no package manager, non-root (uid 1654) by default.
# Runs with a read-only root file system; only /tmp needs to be writable.
# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Company.Service.Api.dll"]
