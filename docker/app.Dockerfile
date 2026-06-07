# =============================================================================
# Dishhive App Dockerfile
# =============================================================================
# Single container hosting both .NET 10 API and Angular 21 SPA.
# Multi-stage build for an optimized production image.
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Build Angular Frontend
# -----------------------------------------------------------------------------
FROM node:22-alpine AS frontend-build
WORKDIR /app

COPY src/dishhive-web/package*.json ./
RUN npm ci

COPY src/dishhive-web/ ./
RUN npm run build -- --configuration production

# -----------------------------------------------------------------------------
# Stage 2: Build .NET API
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

COPY src/Dishhive.Api/Dishhive.Api.csproj ./Dishhive.Api/
RUN dotnet restore ./Dishhive.Api/Dishhive.Api.csproj

COPY src/Dishhive.Api/ ./Dishhive.Api/
WORKDIR /src/Dishhive.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# -----------------------------------------------------------------------------
# Stage 3: Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

RUN groupadd -r appgroup && useradd -r -g appgroup appuser

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /app/dist/dishhive-web/browser ./wwwroot

RUN chown -R appuser:appgroup /app
USER appuser

EXPOSE 5100

ENV ASPNETCORE_URLS=http://+:5100
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection=""

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
  CMD curl -f http://localhost:5100/health || exit 1

ENTRYPOINT ["dotnet", "Dishhive.Api.dll"]
