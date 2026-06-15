# ── Stage 1: Publish ingestion worker ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Packages.props ./
COPY src/ApexRacers.Core/       ./src/ApexRacers.Core/
COPY src/ApexRacers.Data/       ./src/ApexRacers.Data/
COPY src/ApexRacers.Ingestion/  ./src/ApexRacers.Ingestion/
RUN dotnet publish src/ApexRacers.Ingestion/ApexRacers.Ingestion.csproj \
    -c Release -o /publish --no-self-contained

# ── Stage 2: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /publish ./
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "ApexRacers.Ingestion.dll"]
