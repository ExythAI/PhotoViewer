using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PhotoViewer.Api.Data;
using PhotoViewer.Api.Models;

namespace PhotoViewer.Api.Services;

public class ScanProgress
{
    public bool IsScanning { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int TotalFolders { get; set; }
    public int ScannedFolders { get; set; }
    public int NewFiles { get; set; }
    public int UpdatedFiles { get; set; }
    public int DeletedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public double PercentComplete => TotalFiles > 0 ? Math.Round((double)ProcessedFiles / TotalFiles * 100, 1) : 0;
    public string CurrentFile { get; set; } = string.Empty;
    public DateTime? LastScanStarted { get; set; }
    public DateTime? LastScanCompleted { get; set; }
    public string Status { get; set; } = "Idle";
}

public class FileIndexerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FileIndexerService> _logger;

    public static ScanProgress Progress { get; } = new();

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
                Progress.IsScanning = false;
                Progress.Status = $"Scan failed: {ex.Message}";
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

        Progress.IsScanning = true;
        Progress.LastScanStarted = DateTime.UtcNow;
        Progress.Status = "Starting scan...";
        Progress.ProcessedFiles = 0;
        Progress.NewFiles = 0;
        Progress.UpdatedFiles = 0;
        Progress.DeletedFiles = 0;
        Progress.SkippedFiles = 0;

        if (!Directory.Exists(mediaPath))
        {
            _logger.LogWarning("Media path {Path} does not exist, skipping scan", mediaPath);
            Progress.IsScanning = false;
            Progress.Status = "Media path not found";
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbService = scope.ServiceProvider.GetRequiredService<ThumbnailService>();

        // Count folders first
        Progress.Status = "Discovering files...";
        var allFolders = Directory.GetDirectories(mediaPath, "*", SearchOption.AllDirectories);
        Progress.TotalFolders = allFolders.Length + 1; // +1 for root
        Progress.ScannedFolders = 0;

        var allFiles = Directory.EnumerateFiles(mediaPath, "*", SearchOption.AllDirectories)
            .Where(f => ThumbnailService.IsSupported(Path.GetExtension(f)))
            .ToList();

        Progress.TotalFiles = allFiles.Count;
        _logger.LogInformation("Found {Count} supported files in {Folders} folders", allFiles.Count, Progress.TotalFolders);

        Progress.Status = "Indexing files...";

        var existingFiles = await db.MediaFiles
            .Where(m => !m.IsDeleted)
            .ToDictionaryAsync(m => m.RelativePath, ct);

        var processedPaths = new HashSet<string>();
        var processedFolders = new HashSet<string>();
        var newCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;

        // ─── PHASE 1: Fast index (metadata only, no checksums, no thumbnails) ───
        foreach (var filePath in allFiles)
        {
            if (ct.IsCancellationRequested) break;

            var relativePath = Path.GetRelativePath(mediaPath, filePath).Replace('\\', '/');
            processedPaths.Add(relativePath);

            // Track folder progress
            var folder = Path.GetDirectoryName(relativePath) ?? "";
            if (processedFolders.Add(folder))
            {
                Progress.ScannedFolders = processedFolders.Count;
            }

            Progress.ProcessedFiles++;
            Progress.CurrentFile = Path.GetFileName(filePath);

            try
            {
                var fileInfo = new FileInfo(filePath);

                if (existingFiles.TryGetValue(relativePath, out var existing))
                {
                    // Check if file was modified
                    if (existing.FileModifiedAt != fileInfo.LastWriteTimeUtc || existing.FileSize != fileInfo.Length)
                    {
                        UpdateMediaFileMetadata(existing, filePath, fileInfo);
                        updatedCount++;
                        Progress.UpdatedFiles = updatedCount;
                    }
                }
                else
                {
                    // New file — fast create with metadata only
                    var mediaFile = CreateMediaFileFast(filePath, relativePath, fileInfo);
                    db.MediaFiles.Add(mediaFile);
                    newCount++;
                    Progress.NewFiles = newCount;
                }
            }
            catch (Exception ex)
            {
                skippedCount++;
                Progress.SkippedFiles = skippedCount;
                _logger.LogWarning(ex, "Skipped file {File}", filePath);
            }

            // Save in batches of 200
            if ((newCount + updatedCount) % 200 == 0 && (newCount + updatedCount) > 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        // Mark deleted files
        Progress.Status = "Checking for deleted files...";
        var deletedCount = 0;
        foreach (var existing in existingFiles.Values)
        {
            if (!processedPaths.Contains(existing.RelativePath) && !existing.IsDeleted)
            {
                existing.IsDeleted = true;
                deletedCount++;
            }
        }
        Progress.DeletedFiles = deletedCount;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Phase 1 complete: {New} new, {Updated} updated, {Deleted} deleted, {Skipped} skipped",
            newCount, updatedCount, deletedCount, skippedCount);

        // ─── PHASE 2: Generate thumbnails ───
        Progress.Status = "Generating thumbnails...";
        var filesNeedingThumbs = await db.MediaFiles
            .Where(m => !m.IsDeleted && m.ThumbnailPath == null)
            .ToListAsync(ct);

        var thumbsDone = 0;
        var thumbsTotal = filesNeedingThumbs.Count;
        foreach (var file in filesNeedingThumbs)
        {
            if (ct.IsCancellationRequested) break;
            thumbsDone++;
            Progress.CurrentFile = $"Thumbnail {thumbsDone}/{thumbsTotal}: {file.FileName}";

            try
            {
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed thumbnail for {File}", file.FileName);
            }

            // Save every 50 thumbnails
            if (thumbsDone % 50 == 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        await db.SaveChangesAsync(ct);

        // ─── PHASE 3: Compute checksums for files missing them ───
        Progress.Status = "Computing checksums...";
        var filesNeedingChecksum = await db.MediaFiles
            .Where(m => !m.IsDeleted && m.Sha256Checksum == null)
            .ToListAsync(ct);

        var checksumsDone = 0;
        var checksumsTotal = filesNeedingChecksum.Count;
        foreach (var file in filesNeedingChecksum)
        {
            if (ct.IsCancellationRequested) break;
            checksumsDone++;
            Progress.CurrentFile = $"Checksum {checksumsDone}/{checksumsTotal}: {file.FileName}";

            try
            {
                var fullPath = Path.Combine(mediaPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                {
                    file.Sha256Checksum = await ComputeChecksumAsync(fullPath, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed checksum for {File}", file.FileName);
            }

            if (checksumsDone % 50 == 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        await db.SaveChangesAsync(ct);

        Progress.IsScanning = false;
        Progress.LastScanCompleted = DateTime.UtcNow;
        Progress.Status = "Idle";
        Progress.CurrentFile = string.Empty;

        _logger.LogInformation(
            "Scan complete: {New} new, {Updated} updated, {Deleted} deleted, {Thumbs} thumbnails, {Checksums} checksums",
            newCount, updatedCount, deletedCount, thumbsDone, checksumsDone);
    }

    /// <summary>
    /// Fast file creation — only reads FileInfo metadata, no file content reads.
    /// Checksums and thumbnails are deferred to later phases.
    /// </summary>
    private static MediaFile CreateMediaFileFast(string filePath, string relativePath, FileInfo fileInfo)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var mediaType = ThumbnailService.IsImage(extension) ? MediaType.Image : MediaType.Video;

        return new MediaFile
        {
            FileName = Path.GetFileName(filePath),
            RelativePath = relativePath,
            FullPath = filePath,
            Extension = extension,
            FileSize = fileInfo.Length,
            MediaType = mediaType,
            IndexedAt = DateTime.UtcNow,
            FileModifiedAt = fileInfo.LastWriteTimeUtc,
            TakenDate = fileInfo.LastWriteTimeUtc // Use file modified date; EXIF read deferred
        };
    }

    /// <summary>
    /// Lightweight metadata update — no file content reads.
    /// </summary>
    private static void UpdateMediaFileMetadata(MediaFile existing, string filePath, FileInfo fileInfo)
    {
        existing.FileSize = fileInfo.Length;
        existing.FileModifiedAt = fileInfo.LastWriteTimeUtc;
        existing.IndexedAt = DateTime.UtcNow;
        existing.IsDeleted = false;
        existing.TakenDate = fileInfo.LastWriteTimeUtc;
        // Clear checksum/thumbnail so they get regenerated
        existing.Sha256Checksum = null;
        existing.ThumbnailPath = null;
    }

    private static async Task<string> ComputeChecksumAsync(string filePath, CancellationToken ct = default)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 
            bufferSize: 81920); // 80KB buffer for network performance
        var hash = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
