using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PhotoViewer.Api.Data;
using PhotoViewer.Api.Models;

namespace PhotoViewer.Api.Services;

public class FileIndexerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FileIndexerService> _logger;

    public FileIndexerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<FileIndexerService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for the app to fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunScanAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during file scan");
            }

            var intervalMinutes = _config.GetValue("Scanner:IntervalMinutes", 60);
            _logger.LogInformation("Next scan in {Minutes} minutes", intervalMinutes);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    public async Task RunScanAsync(CancellationToken ct = default)
    {
        var mediaPath = _config["Storage:MediaPath"] ?? "/media";
        _logger.LogInformation("Starting file scan of {Path}", mediaPath);

        if (!Directory.Exists(mediaPath))
        {
            _logger.LogWarning("Media path {Path} does not exist, skipping scan", mediaPath);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbService = scope.ServiceProvider.GetRequiredService<ThumbnailService>();

        var allFiles = Directory.EnumerateFiles(mediaPath, "*", SearchOption.AllDirectories)
            .Where(f => ThumbnailService.IsSupported(Path.GetExtension(f)))
            .ToList();

        _logger.LogInformation("Found {Count} supported files", allFiles.Count);

        var existingFiles = await db.MediaFiles
            .Where(m => !m.IsDeleted)
            .ToDictionaryAsync(m => m.RelativePath, ct);

        var processedPaths = new HashSet<string>();
        var newCount = 0;
        var updatedCount = 0;

        foreach (var filePath in allFiles)
        {
            if (ct.IsCancellationRequested) break;

            var relativePath = Path.GetRelativePath(mediaPath, filePath).Replace('\\', '/');
            processedPaths.Add(relativePath);

            var fileInfo = new FileInfo(filePath);

            if (existingFiles.TryGetValue(relativePath, out var existing))
            {
                // Check if file was modified
                if (existing.FileModifiedAt != fileInfo.LastWriteTimeUtc || existing.FileSize != fileInfo.Length)
                {
                    await UpdateMediaFileAsync(existing, filePath, fileInfo, thumbService);
                    updatedCount++;
                }
                else if (string.IsNullOrEmpty(existing.ThumbnailPath))
                {
                    // Generate missing thumbnail
                    var thumbPath = await thumbService.GenerateThumbnailAsync(filePath, existing.Id, existing.Extension);
                    if (thumbPath != null)
                    {
                        existing.ThumbnailPath = thumbPath;
                    }
                }
            }
            else
            {
                // New file
                var mediaFile = await CreateMediaFileAsync(filePath, relativePath, fileInfo, thumbService);
                db.MediaFiles.Add(mediaFile);
                newCount++;
            }

            // Save in batches of 100
            if ((newCount + updatedCount) % 100 == 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        // Mark deleted files
        var deletedCount = 0;
        foreach (var existing in existingFiles.Values)
        {
            if (!processedPaths.Contains(existing.RelativePath) && !existing.IsDeleted)
            {
                existing.IsDeleted = true;
                deletedCount++;
            }
        }

        await db.SaveChangesAsync(ct);

        // Generate thumbnails for new files (need IDs after save)
        var newFilesWithoutThumbs = await db.MediaFiles
            .Where(m => !m.IsDeleted && m.ThumbnailPath == null)
            .ToListAsync(ct);

        foreach (var file in newFilesWithoutThumbs)
        {
            if (ct.IsCancellationRequested) break;

            var fullPath = Path.Combine(mediaPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                var thumbPath = await thumbService.GenerateThumbnailAsync(fullPath, file.Id, file.Extension);
                if (thumbPath != null)
                {
                    file.ThumbnailPath = thumbPath;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Scan complete: {New} new, {Updated} updated, {Deleted} deleted",
            newCount, updatedCount, deletedCount);
    }

    private async Task<MediaFile> CreateMediaFileAsync(
        string filePath, string relativePath, FileInfo fileInfo, ThumbnailService thumbService)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var mediaType = ThumbnailService.IsImage(extension) ? MediaType.Image : MediaType.Video;

        var mediaFile = new MediaFile
        {
            FileName = Path.GetFileName(filePath),
            RelativePath = relativePath,
            FullPath = filePath,
            Extension = extension,
            FileSize = fileInfo.Length,
            MediaType = mediaType,
            IndexedAt = DateTime.UtcNow,
            FileModifiedAt = fileInfo.LastWriteTimeUtc
        };

        // Compute checksum
        mediaFile.Sha256Checksum = await ComputeChecksumAsync(filePath);

        // Get dimensions/duration
        if (mediaType == MediaType.Image)
        {
            var (w, h) = thumbService.GetImageDimensions(filePath);
            mediaFile.Width = w;
            mediaFile.Height = h;
        }
        else
        {
            mediaFile.DurationSeconds = thumbService.GetVideoDuration(filePath);
        }

        // Try to get taken date from EXIF
        mediaFile.TakenDate = GetTakenDate(filePath) ?? fileInfo.LastWriteTimeUtc;

        return mediaFile;
    }

    private async Task UpdateMediaFileAsync(
        MediaFile existing, string filePath, FileInfo fileInfo, ThumbnailService thumbService)
    {
        existing.FileSize = fileInfo.Length;
        existing.FileModifiedAt = fileInfo.LastWriteTimeUtc;
        existing.IndexedAt = DateTime.UtcNow;
        existing.Sha256Checksum = await ComputeChecksumAsync(filePath);
        existing.IsDeleted = false;

        if (existing.MediaType == MediaType.Image)
        {
            var (w, h) = thumbService.GetImageDimensions(filePath);
            existing.Width = w;
            existing.Height = h;
        }
        else
        {
            existing.DurationSeconds = thumbService.GetVideoDuration(filePath);
        }

        existing.TakenDate = GetTakenDate(filePath) ?? fileInfo.LastWriteTimeUtc;

        // Regenerate thumbnail
        var thumbDir = Path.GetDirectoryName(existing.ThumbnailPath ?? "");
        if (thumbDir != null && existing.ThumbnailPath != null && File.Exists(existing.ThumbnailPath))
            File.Delete(existing.ThumbnailPath);

        existing.ThumbnailPath = await thumbService.GenerateThumbnailAsync(filePath, existing.Id, existing.Extension);
    }

    private static async Task<string> ComputeChecksumAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DateTime? GetTakenDate(string filePath)
    {
        try
        {
            var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(filePath);
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    if (tag.Name == "Date/Time Original" || tag.Name == "Date/Time")
                    {
                        if (DateTime.TryParse(tag.Description, out var date))
                            return date;
                    }
                }
            }
        }
        catch { }

        return null;
    }
}
