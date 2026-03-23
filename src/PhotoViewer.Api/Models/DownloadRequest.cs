namespace PhotoViewer.Api.Models;

public enum DownloadStatus
{
    Pending,
    Processing,
    Ready,
    Expired
}

public class DownloadRequest
{
    public int Id { get; set; }
    public string MediaFileIds { get; set; } = string.Empty; // JSON array of IDs
    public DownloadStatus Status { get; set; }
    public string? ZipFilePath { get; set; }
    public string? ZipFileName { get; set; }
    public long? ZipFileSize { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
