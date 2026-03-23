namespace PhotoViewer.Api.Models;

public class MediaSource
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
