using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoViewer.Api.Data;
using PhotoViewer.Api.Models;
using PhotoViewer.Api.Services;

namespace PhotoViewer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MediaController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ThumbnailService _thumbService;
    private readonly FileIndexerService _indexerService;
    private readonly IConfiguration _config;

    public MediaController(
        AppDbContext db,
        ThumbnailService thumbService,
        FileIndexerService indexerService,
        IConfiguration config)
    {
        _db = db;
        _thumbService = thumbService;
        _indexerService = indexerService;
        _config = config;
    }

    public record MediaListResponse(
        List<MediaFileDto> Items,
        int TotalCount,
        int Page,
        int PageSize);

    public record MediaFileDto(
        int Id,
        string FileName,
        string RelativePath,
        string Extension,
        long FileSize,
        string MediaType,
        int? Width,
        int? Height,
        double? DurationSeconds,
        DateTime? TakenDate,
        DateTime IndexedAt,
        bool HasThumbnail,
        string? Checksum);

    [HttpGet]
    public async Task<IActionResult> GetMedia(
        [FromQuery] string? search = null,
        [FromQuery] string? folder = null,
        [FromQuery] string? type = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] bool hideDuplicates = true,
        [FromQuery] string? sortBy = "takenDate",
        [FromQuery] string? sortDir = "desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.MediaFiles.Where(m => !m.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(m => m.FileName.ToLower().Contains(searchLower));
        }

        if (!string.IsNullOrWhiteSpace(folder))
        {
            query = query.Where(m => m.RelativePath.StartsWith(folder));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (Enum.TryParse<MediaType>(type, true, out var mediaType))
                query = query.Where(m => m.MediaType == mediaType);
        }

        if (DateTime.TryParse(dateFrom, out var from))
        {
            query = query.Where(m => m.TakenDate >= from || (m.TakenDate == null && m.IndexedAt >= from));
        }

        if (DateTime.TryParse(dateTo, out var to))
        {
            var toEnd = to.Date.AddDays(1); // Include the entire "to" day
            query = query.Where(m => m.TakenDate < toEnd || (m.TakenDate == null && m.IndexedAt < toEnd));
        }

        // Hide duplicates: keep only the first file (lowest ID) per checksum
        if (hideDuplicates)
        {
            var duplicateIdsToHide = _db.MediaFiles
                .Where(m => !m.IsDeleted && m.Sha256Checksum != null)
                .GroupBy(m => m.Sha256Checksum)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.OrderBy(m => m.Id).Skip(1).Select(m => m.Id));

            query = query.Where(m => !duplicateIdsToHide.Contains(m.Id));
        }

        var totalCount = await query.CountAsync();

        query = sortBy?.ToLower() switch
        {
            "name" => sortDir == "asc" ? query.OrderBy(m => m.FileName) : query.OrderByDescending(m => m.FileName),
            "size" => sortDir == "asc" ? query.OrderBy(m => m.FileSize) : query.OrderByDescending(m => m.FileSize),
            "indexed" => sortDir == "asc" ? query.OrderBy(m => m.IndexedAt) : query.OrderByDescending(m => m.IndexedAt),
            _ => sortDir == "asc" ? query.OrderBy(m => m.TakenDate) : query.OrderByDescending(m => m.TakenDate)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MediaFileDto(
                m.Id, m.FileName, m.RelativePath, m.Extension,
                m.FileSize, m.MediaType.ToString(),
                m.Width, m.Height, m.DurationSeconds,
                m.TakenDate, m.IndexedAt,
                m.ThumbnailPath != null,
                m.Sha256Checksum))
            .ToListAsync();

        return Ok(new MediaListResponse(items, totalCount, page, pageSize));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetMediaFile(int id)
    {
        var file = await _db.MediaFiles.FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
        if (file == null) return NotFound();

        return Ok(new MediaFileDto(
            file.Id, file.FileName, file.RelativePath, file.Extension,
            file.FileSize, file.MediaType.ToString(),
            file.Width, file.Height, file.DurationSeconds,
            file.TakenDate, file.IndexedAt,
            file.ThumbnailPath != null,
            file.Sha256Checksum));
    }

    [HttpGet("{id}/thumbnail")]
    [AllowAnonymous]
    public async Task<IActionResult> GetThumbnail(int id)
    {
        var file = await _db.MediaFiles.FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
        if (file == null) return NotFound();

        if (file.ThumbnailPath != null && System.IO.File.Exists(file.ThumbnailPath))
        {
            return PhysicalFile(file.ThumbnailPath, "image/webp");
        }

        // Try generating on the fly
        var mediaPath = _config["Storage:MediaPath"] ?? "/media";
        var fullPath = Path.Combine(mediaPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var thumbPath = await _thumbService.GenerateThumbnailAsync(fullPath, file.Id, file.Extension);
        if (thumbPath != null)
        {
            file.ThumbnailPath = thumbPath;
            await _db.SaveChangesAsync();
            return PhysicalFile(thumbPath, "image/webp");
        }

        return NotFound();
    }

    [HttpGet("{id}/stream")]
    [AllowAnonymous]
    public async Task<IActionResult> StreamFile(int id)
    {
        var file = await _db.MediaFiles.FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
        if (file == null) return NotFound();

        var mediaPath = _config["Storage:MediaPath"] ?? "/media";
        var fullPath = Path.Combine(mediaPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var contentType = file.MediaType == MediaType.Video
            ? GetVideoContentType(file.Extension)
            : GetImageContentType(file.Extension);

        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    [HttpGet("folders")]
    public async Task<IActionResult> GetFolders()
    {
        var paths = await _db.MediaFiles
            .Where(m => !m.IsDeleted)
            .Select(m => m.RelativePath)
            .ToListAsync();

        var folders = paths
            .Select(p => Path.GetDirectoryName(p)?.Replace('\\', '/') ?? "")
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct()
            .OrderBy(f => f)
            .ToList();

        return Ok(folders);
    }

    [HttpGet("duplicates")]
    public async Task<IActionResult> GetDuplicates()
    {
        var duplicates = await _db.MediaFiles
            .Where(m => !m.IsDeleted && m.Sha256Checksum != null)
            .GroupBy(m => m.Sha256Checksum)
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                Checksum = g.Key,
                Count = g.Count(),
                Files = g.Select(m => new MediaFileDto(
                    m.Id, m.FileName, m.RelativePath, m.Extension,
                    m.FileSize, m.MediaType.ToString(),
                    m.Width, m.Height, m.DurationSeconds,
                    m.TakenDate, m.IndexedAt,
                    m.ThumbnailPath != null,
                    m.Sha256Checksum)).ToList()
            })
            .ToListAsync();

        return Ok(duplicates);
    }

    [HttpGet("scan/status")]
    public IActionResult GetScanStatus()
    {
        return Ok(FileIndexerService.Progress);
    }

    // Scan/stop/clear moved to AdminController
    // scan/status kept here for gallery polling (read-only)

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalFiles = await _db.MediaFiles.CountAsync(m => !m.IsDeleted);
        var totalImages = await _db.MediaFiles.CountAsync(m => !m.IsDeleted && m.MediaType == MediaType.Image);
        var totalVideos = await _db.MediaFiles.CountAsync(m => !m.IsDeleted && m.MediaType == MediaType.Video);
        var totalSize = await _db.MediaFiles.Where(m => !m.IsDeleted).SumAsync(m => m.FileSize);
        var duplicateGroups = await _db.MediaFiles
            .Where(m => !m.IsDeleted && m.Sha256Checksum != null)
            .GroupBy(m => m.Sha256Checksum)
            .CountAsync(g => g.Count() > 1);

        return Ok(new
        {
            totalFiles,
            totalImages,
            totalVideos,
            totalSize,
            duplicateGroups
        });
    }

    private static string GetVideoContentType(string ext) => ext.ToLower() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".mkv" => "video/x-matroska",
        ".wmv" => "video/x-ms-wmv",
        _ => "application/octet-stream"
    };

    private static string GetImageContentType(string ext) => ext.ToLower() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".tiff" or ".tif" => "image/tiff",
        _ => "application/octet-stream"
    };
}
