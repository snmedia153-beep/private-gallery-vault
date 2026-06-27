namespace PrivateGalleryVault.Models;

public sealed class MediaItem
{
    public string Id { get; set; } = string.Empty;
    public string TopicId { get; set; } = string.Empty;
    public MediaKind Kind { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string ObjectPath { get; set; } = string.Empty;
    public string ThumbPath { get; set; } = string.Empty;
    public string ThumbSourcePath { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
    public bool Favorite { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
