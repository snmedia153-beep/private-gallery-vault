namespace PrivateGalleryVault.Models;

public sealed class TopicFolder
{
    public string Id { get; set; } = string.Empty;
    public string TopicId { get; set; } = string.Empty;
    public string? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int ChildFolderCount { get; set; }
    public int MediaCount { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public override string ToString() => Name;
}
