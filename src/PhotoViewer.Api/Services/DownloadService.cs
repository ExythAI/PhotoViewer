using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoViewer.Api.Data;
using PhotoViewer.Api.Models;

namespace PhotoViewer.Api.Services;

public class DownloadService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DownloadService> _logger;
    private readonly string _downloadDir;

    public DownloadService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<DownloadService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _downloadDir = config["Storage:DownloadPath"] ?? "/data/downloads";
        Directory.CreateDirectory(_downloadDir);
    }

    public async Task<DownloadRequest> CreateRequestAsync(List<int> mediaFileIds, int userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var request = new DownloadRequest
        {
            MediaFileIds = JsonSerializer.Serialize(mediaFileIds),
            Status = DownloadStatus.Pending,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        db.DownloadRequests.Add(request);
        await db.SaveChangesAsync();

        // Start processing in background
        _ = Task.Run(() => ProcessRequestAsync(request.Id));

        return request;
    }

    private async Task ProcessRequestAsync(int requestId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var request = await db.DownloadRequests.FindAsync(requestId);
        if (request == null) return;

        try
        {
            request.Status = DownloadStatus.Processing;
            await db.SaveChangesAsync();

            var mediaFileIds = JsonSerializer.Deserialize<List<int>>(request.MediaFileIds) ?? new();
            var mediaFiles = await db.MediaFiles
                .Where(m => mediaFileIds.Contains(m.Id) && !m.IsDeleted)
                .ToListAsync();

            var zipFileName = $"PhotoViewer_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
            var zipPath = Path.Combine(_downloadDir, $"{requestId}_{zipFileName}");

            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var file in mediaFiles)
                {
                    if (File.Exists(file.FullPath))
                    {
                        var entryName = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                        archive.CreateEntryFromFile(file.FullPath, entryName, CompressionLevel.Fastest);
                    }
                }
            }

            var zipInfo = new FileInfo(zipPath);
            request.ZipFilePath = zipPath;
            request.ZipFileName = zipFileName;
            request.ZipFileSize = zipInfo.Length;
            request.Status = DownloadStatus.Ready;
            request.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create zip for request {Id}", requestId);
            request.Status = DownloadStatus.Expired;
        }

        await db.SaveChangesAsync();
    }

    public async Task<DownloadRequest?> GetRequestAsync(int requestId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DownloadRequests.FindAsync(requestId);
    }

    public async Task CleanupExpiredAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expiredTime = DateTime.UtcNow.AddHours(-1);
        var expired = await db.DownloadRequests
            .Where(r => r.Status == DownloadStatus.Ready && r.CompletedAt < expiredTime)
            .ToListAsync();

        foreach (var request in expired)
        {
            if (request.ZipFilePath != null && File.Exists(request.ZipFilePath))
                File.Delete(request.ZipFilePath);

            request.Status = DownloadStatus.Expired;
        }

        await db.SaveChangesAsync();
    }
}
