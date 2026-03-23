using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoViewer.Api.Data;
using PhotoViewer.Api.Models;
using PhotoViewer.Api.Services;

namespace PhotoViewer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly FileIndexerService _indexerService;

    public AdminController(AppDbContext db, IConfiguration config, FileIndexerService indexerService)
    {
        _db = db;
        _config = config;
        _indexerService = indexerService;
    }

    // ─── MEDIA SOURCES ───

    [HttpGet("sources")]
    public async Task<IActionResult> GetSources()
    {
        var sources = await _db.MediaSources.OrderBy(s => s.Id).ToListAsync();
        return Ok(sources);
    }

    [HttpPost("sources")]
    public async Task<IActionResult> AddSource([FromBody] AddSourceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest(new { message = "Path is required" });

        var path = request.Path.Trim();

        // Check if already exists
        if (await _db.MediaSources.AnyAsync(s => s.Path == path))
            return BadRequest(new { message = "This path is already configured" });

        var source = new MediaSource
        {
            Path = path,
            Label = string.IsNullOrWhiteSpace(request.Label) ? System.IO.Path.GetFileName(path.TrimEnd('/','\\')) : request.Label.Trim(),
            IsActive = true
        };

        _db.MediaSources.Add(source);
        await _db.SaveChangesAsync();

        return Ok(source);
    }

    [HttpDelete("sources/{id}")]
    public async Task<IActionResult> DeleteSource(int id)
    {
        var source = await _db.MediaSources.FindAsync(id);
        if (source == null)
            return NotFound(new { message = "Source not found" });

        _db.MediaSources.Remove(source);
        await _db.SaveChangesAsync();

        return Ok(new { message = $"Removed source: {source.Label}" });
    }

    [HttpPut("sources/{id}/toggle")]
    public async Task<IActionResult> ToggleSource(int id)
    {
        var source = await _db.MediaSources.FindAsync(id);
        if (source == null)
            return NotFound(new { message = "Source not found" });

        source.IsActive = !source.IsActive;
        await _db.SaveChangesAsync();

        return Ok(source);
    }

    // ─── SCAN CONTROLS ───

    [HttpPost("scan/start")]
    public IActionResult StartScan()
    {
        if (FileIndexerService.Progress.IsScanning)
            return BadRequest(new { message = "A scan is already running" });

        _ = Task.Run(() => _indexerService.RunScanAsync());
        return Ok(new { message = "Scan started" });
    }

    [HttpPost("scan/stop")]
    public IActionResult StopScan()
    {
        if (!FileIndexerService.Progress.IsScanning)
            return BadRequest(new { message = "No scan is running" });

        FileIndexerService.StopScan();
        return Ok(new { message = "Scan stop requested" });
    }

    [HttpGet("scan/status")]
    public IActionResult GetScanStatus()
    {
        return Ok(FileIndexerService.Progress);
    }

    // ─── CLEAR DATABASE ───

    [HttpPost("clear")]
    public async Task<IActionResult> ClearDatabase()
    {
        if (FileIndexerService.Progress.IsScanning)
            return BadRequest(new { message = "Cannot clear while a scan is running" });

        var mediaCount = await _db.MediaFiles.CountAsync();
        _db.MediaFiles.RemoveRange(_db.MediaFiles);
        _db.DownloadRequests.RemoveRange(_db.DownloadRequests);
        await _db.SaveChangesAsync();

        var thumbDir = _config["Storage:ThumbnailPath"] ?? "/data/thumbnails";
        if (Directory.Exists(thumbDir))
        {
            foreach (var file in Directory.GetFiles(thumbDir))
            {
                try { System.IO.File.Delete(file); } catch { }
            }
        }

        var downloadDir = _config["Storage:DownloadPath"] ?? "/data/downloads";
        if (Directory.Exists(downloadDir))
        {
            foreach (var file in Directory.GetFiles(downloadDir))
            {
                try { System.IO.File.Delete(file); } catch { }
            }
        }

        return Ok(new { message = $"Cleared {mediaCount} media files, thumbnails, and downloads" });
    }

    public record AddSourceRequest(string Path, string? Label);
}
