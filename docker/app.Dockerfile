# =============================================================================
# Dishhive App Dockerfile
# =============================================================================
# Single container hosting both .NET 10 API and Angular 21 PWA
# Multi-stage build for optimized production image
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Build Angular Frontend
# -----------------------------------------------------------------------------
FROM node:22-alpine AS frontend-build
WORKDIR /app

# Copy package files and install dependencies
COPY src/dishhive-web/package*.json ./
RUN npm ci

# Copy source code and build (sync-version runs as part of build script)
COPY src/dishhive-web/ ./
COPY src/Dishhive.Api/Dishhive.Api.csproj /backend/Dishhive.Api.csproj
RUN npm run build -- --configuration production

# -----------------------------------------------------------------------------
# Stage 2: Build .NET API
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

# Copy project file and restore dependencies
COPY src/Dishhive.Api/Dishhive.Api.csproj ./Dishhive.Api/
RUN dotnet restore ./Dishhive.Api/Dishhive.Api.csproj

# Copy source code and publish
COPY src/Dishhive.Api/ ./Dishhive.Api/
WORKDIR /src/Dishhive.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# -----------------------------------------------------------------------------
# Stage 3: Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN groupadd -r appgroup && useradd -r -g appgroup appuser

# Copy published .NET app
COPY --from=backend-build /app/publish .

# Copy Angular build output to wwwroot
COPY --from=frontend-build /app/dist/dishhive-web/browser ./wwwroot

# Set ownership and switch to non-root user
RUN chown -R appuser:appgroup /app
USER appuser

# Expose port
EXPOSE 5100

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5100
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection=""

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
  CMD curl -f http://localhost:5100/health || exit 1

# Start the application
ENTRYPOINT ["dotnet", "Dishhive.Api.dll"]
