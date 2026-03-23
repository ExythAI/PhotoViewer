using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoViewer.Api.Services;

namespace PhotoViewer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DownloadController : ControllerBase
{
    private readonly DownloadService _downloadService;

    public DownloadController(DownloadService downloadService)
    {
        _downloadService = downloadService;
    }

    public record CreateDownloadRequest(List<int> MediaFileIds);

    [HttpPost]
    public async Task<IActionResult> CreateDownload([FromBody] CreateDownloadRequest request)
    {
        if (request.MediaFileIds == null || request.MediaFileIds.Count == 0)
            return BadRequest(new { message = "No files selected" });

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var downloadRequest = await _downloadService.CreateRequestAsync(request.MediaFileIds, userId);

        return Ok(new
        {
            downloadRequest.Id,
            Status = downloadRequest.Status.ToString()
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetStatus(int id)
    {
        var request = await _downloadService.GetRequestAsync(id);
        if (request == null) return NotFound();

        return Ok(new
        {
            request.Id,
            Status = request.Status.ToString(),
            request.ZipFileName,
            request.ZipFileSize,
            request.CreatedAt,
            request.CompletedAt
        });
    }

    [HttpGet("{id}/file")]
    public async Task<IActionResult> DownloadFile(int id)
    {
        var request = await _downloadService.GetRequestAsync(id);
        if (request == null) return NotFound();

        if (request.Status != Models.DownloadStatus.Ready || request.ZipFilePath == null)
            return BadRequest(new { message = "Download not ready" });

        if (!System.IO.File.Exists(request.ZipFilePath))
            return NotFound(new { message = "Zip file not found" });

        return PhysicalFile(
            request.ZipFilePath,
            "application/zip",
            request.ZipFileName ?? "download.zip");
    }
}
