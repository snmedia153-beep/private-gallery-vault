namespace PrivateGalleryVault.Models;

public sealed class Topic
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? CoverMediaId { get; set; }
    public int SortOrder { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public override string ToString() => Name;
}
