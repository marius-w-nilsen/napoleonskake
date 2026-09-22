# ---- Build -------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until the csproj changes.
COPY src/NapoleonBot/NapoleonBot.csproj src/NapoleonBot/
RUN dotnet restore src/NapoleonBot/NapoleonBot.csproj

COPY src/ src/
RUN dotnet publish src/NapoleonBot/NapoleonBot.csproj -c Release -o /app --no-restore

# ---- Runtime -----------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is only for the container health check.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# The SQLite file lives in /app/db; mount a volume there. Owned by the non-root "app" user the base image provides.
RUN mkdir -p /app/db && chown -R app:app /app
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__ConnectionString="Data Source=/app/db/napoleon.db"

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "NapoleonBot.dll"]
