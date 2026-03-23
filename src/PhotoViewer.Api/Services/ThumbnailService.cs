using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Webp;
using System.Diagnostics;

namespace PhotoViewer.Api.Services;

public class ThumbnailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly string _thumbnailDir;
    private const int ThumbnailWidth = 400;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".webm", ".m4v", ".3gp"
    };

    public ThumbnailService(IConfiguration config, ILogger<ThumbnailService> logger)
    {
        _config = config;
        _logger = logger;
        _thumbnailDir = _config["Storage:ThumbnailPath"] ?? "/data/thumbnails";
        Directory.CreateDirectory(_thumbnailDir);
    }

    public static bool IsImage(string extension) => ImageExtensions.Contains(extension);
    public static bool IsVideo(string extension) => VideoExtensions.Contains(extension);
    public static bool IsSupported(string extension) => IsImage(extension) || IsVideo(extension);

    public async Task<string?> GenerateThumbnailAsync(string sourceFilePath, int fileId, string extension)
    {
        var outputPath = Path.Combine(_thumbnailDir, $"{fileId}.webp");

        if (File.Exists(outputPath))
            return outputPath;

        try
        {
            if (IsImage(extension))
            {
                return await GenerateImageThumbnailAsync(sourceFilePath, outputPath);
            }
            else if (IsVideo(extension))
            {
                return await GenerateVideoThumbnailAsync(sourceFilePath, outputPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail for {File}", sourceFilePath);
        }

        return null;
    }

    private async Task<string?> GenerateImageThumbnailAsync(string sourcePath, string outputPath)
    {
        using var image = await Image.LoadAsync(sourcePath);
        var ratio = (double)ThumbnailWidth / image.Width;
        var height = (int)(image.Height * ratio);

        image.Mutate(x => x.Resize(ThumbnailWidth, height));
        await image.SaveAsync(outputPath, new WebpEncoder { Quality = 80 });

        return outputPath;
    }

    private async Task<string?> GenerateVideoThumbnailAsync(string sourcePath, string outputPath)
    {
        // Extract frame at 1 second using ffmpeg
        var tempFrame = Path.Combine(_thumbnailDir, $"temp_{Guid.NewGuid()}.png");

        if (!await RunFfmpegWithTimeout(
            $"-i \"{sourcePath}\" -ss 00:00:01 -vframes 1 -y \"{tempFrame}\"", 30))
        {
            // Try frame at 0 seconds if 1 second fails (short video)
            await RunFfmpegWithTimeout(
                $"-i \"{sourcePath}\" -ss 00:00:00 -vframes 1 -y \"{tempFrame}\"", 30);
        }

        if (File.Exists(tempFrame))
        {
            var result = await GenerateImageThumbnailAsync(tempFrame, outputPath);
            try { File.Delete(tempFrame); } catch { }
            return result;
        }

        return null;
    }

    private static async Task<bool> RunFfmpegWithTimeout(string arguments, int timeoutSeconds)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        // Drain stderr to prevent deadlock
        _ = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            try { process.Kill(true); } catch { }
            return false;
        }

        return process.ExitCode == 0;
    }

    public (int? width, int? height) GetImageDimensions(string filePath)
    {
        try
        {
            var info = Image.Identify(filePath);
            return (info?.Width, info?.Height);
        }
        catch
        {
            return (null, null);
        }
    }

    public double? GetVideoDuration(string filePath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                try { process.Kill(); } catch { }
                return null;
            }

            if (double.TryParse(output.Trim(), out var duration))
                return duration;
        }
        catch { }

        return null;
    }
}
