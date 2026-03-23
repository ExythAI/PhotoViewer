# Multi-stage Dockerfile for PhotoViewer

# ---- Stage 1: Build the frontend SPA ----
FROM node:20-alpine AS frontend-build
WORKDIR /build
COPY src/PhotoViewer.Web/package*.json ./
RUN npm ci
COPY src/PhotoViewer.Web/ ./
RUN npm run build

# ---- Stage 2: Build the .NET backend ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /build
COPY PhotoViewer.sln ./
COPY src/PhotoViewer.Api/ src/PhotoViewer.Api/
RUN dotnet publish src/PhotoViewer.Api/PhotoViewer.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-self-contained

# ---- Stage 3: Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install ffmpeg for video thumbnail generation
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published .NET app
COPY --from=backend-build /app/publish .

# Copy frontend build into wwwroot
COPY --from=frontend-build /build/dist wwwroot/

# Create data directories
RUN mkdir -p /data/thumbnails /data/downloads /media

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__MediaPath=/media
ENV Storage__DatabasePath=/data/photoviewer.db
ENV Storage__ThumbnailPath=/data/thumbnails
ENV Storage__DownloadPath=/data/downloads
ENV Scanner__IntervalMinutes=60

ENTRYPOINT ["dotnet", "PhotoViewer.Api.dll"]
