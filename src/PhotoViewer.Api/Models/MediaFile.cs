namespace PhotoViewer.Api.Models;

public enum MediaType
{
    Image,
    Video
}

public class MediaFile
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? Sha256Checksum { get; set; }
    public MediaType MediaType { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTime? TakenDate { get; set; }
    public DateTime IndexedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }
    public string? ThumbnailPath { get; set; }
    public bool IsDeleted { get; set; }
}
