# ── Stage 1: Build React frontend ────────────────────────────────────────────
FROM node:26-alpine AS frontend
WORKDIR /app
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build
# Output: /app/dist

# ── Stage 2: Publish .NET API ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY Directory.Packages.props ./
COPY src/ApexRacers.Core/      ./src/ApexRacers.Core/
COPY src/ApexRacers.Data/      ./src/ApexRacers.Data/
COPY src/ApexRacers.Api/       ./src/ApexRacers.Api/
RUN dotnet publish src/ApexRacers.Api/ApexRacers.Api.csproj \
    -c Release -o /publish --no-self-contained
# Output: /publish

# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api-build /publish     ./
COPY --from=frontend  /app/dist    ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# The aspnet image ships a non-root `app` user (UID 1654 via APP_UID) but does not
# switch to it. Root in the container turns any future RCE into a container-escape
# problem, and 8080 is unprivileged, so nothing here needs it.
USER $APP_UID
ENTRYPOINT ["dotnet", "ApexRacers.Api.dll"]
