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
    private static CancellationTokenSource? _scanCts;

    public FileIndexerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<FileIndexerService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public static void StopScan()
    {
        _scanCts?.Cancel();
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
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Scan was cancelled");
                Progress.IsScanning = false;
                Progress.Status = "Scan cancelled";
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
        // Create a linked CTS so both the host shutdown and manual stop work
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _scanCts.Token;

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
            .ToDictionaryAsync(m => m.RelativePath, token);

        var processedPaths = new HashSet<string>();
        var processedFolders = new HashSet<string>();
        var newCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;

        // ─── PHASE 1: Fast index (metadata only, no checksums, no thumbnails) ───
        foreach (var filePath in allFiles)
        {
            if (token.IsCancellationRequested) break;

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
                await db.SaveChangesAsync(token);
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

        await db.SaveChangesAsync(token);

        _logger.LogInformation(
            "Phase 1 complete: {New} new, {Updated} updated, {Deleted} deleted, {Skipped} skipped",
            newCount, updatedCount, deletedCount, skippedCount);

        // ─── PHASE 2: Generate thumbnails (parallel, 6 workers) ───
        Progress.Status = "Generating thumbnails...";
        var filesNeedingThumbs = await db.MediaFiles
            .Where(m => !m.IsDeleted && m.ThumbnailPath == null)
            .ToListAsync(token);

        var thumbsDone = 0;
        var thumbsTotal = filesNeedingThumbs.Count;
        Progress.TotalFiles = thumbsTotal;
        Progress.ProcessedFiles = 0;

        // Collect results from parallel workers
        var thumbResults = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();

        await Parallel.ForEachAsync(filesNeedingThumbs,
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = token },
            async (file, pToken) =>
            {
                var done = Interlocked.Increment(ref thumbsDone);
                Progress.ProcessedFiles = done;
                Progress.CurrentFile = $"Thumbnail {done}/{thumbsTotal}: {file.FileName}";

                try
                {
                    var fullPath = Path.Combine(mediaPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath))
                    {
                        var thumbPath = await thumbService.GenerateThumbnailAsync(fullPath, file.Id, file.Extension);
                        if (thumbPath != null)
                        {
                            thumbResults[file.Id] = thumbPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed thumbnail for {File}", file.FileName);
                }
            });

        // Apply results to DB (single-threaded)
        foreach (var (fileId, thumbPath) in thumbResults)
        {
            var file = filesNeedingThumbs.FirstOrDefault(f => f.Id == fileId);
            if (file != null) file.ThumbnailPath = thumbPath;
        }
        await db.SaveChangesAsync(token);

        _logger.LogInformation("Phase 2 complete: {Thumbs}/{Total} thumbnails generated", thumbResults.Count, thumbsTotal);

        // ─── PHASE 3: Compute checksums (parallel, 3 workers) ───
        Progress.Status = "Computing checksums...";
        var filesNeedingChecksum = await db.MediaFiles
            .Where(m => !m.IsDeleted && m.Sha256Checksum == null)
            .ToListAsync(token);

        var checksumsDone = 0;
        var checksumsTotal = filesNeedingChecksum.Count;
        Progress.TotalFiles = checksumsTotal;
        Progress.ProcessedFiles = 0;

        var checksumResults = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();

        await Parallel.ForEachAsync(filesNeedingChecksum,
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token },
            async (file, pToken) =>
            {
                var done = Interlocked.Increment(ref checksumsDone);
                Progress.ProcessedFiles = done;
                Progress.CurrentFile = $"Checksum {done}/{checksumsTotal}: {file.FileName}";

                try
                {
                    var fullPath = Path.Combine(mediaPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath))
                    {
                        var checksum = await ComputeChecksumAsync(fullPath, pToken);
                        checksumResults[file.Id] = checksum;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed checksum for {File}", file.FileName);
                }
            });

        // Apply results to DB (single-threaded)
        foreach (var (fileId, checksum) in checksumResults)
        {
            var file = filesNeedingChecksum.FirstOrDefault(f => f.Id == fileId);
            if (file != null) file.Sha256Checksum = checksum;
        }
        await db.SaveChangesAsync(token);

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
