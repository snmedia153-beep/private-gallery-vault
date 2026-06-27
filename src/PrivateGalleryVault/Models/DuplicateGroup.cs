namespace PrivateGalleryVault.Models;

public sealed class DuplicateGroup
{
    public string SourceHash { get; set; } = string.Empty;
    public int Count { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<MediaItem> Items { get; set; } = [];

    public long EstimatedWastedBytes
    {
        get
        {
            if (Items.Count <= 1)
                return 0;

            var keep = Items.OrderBy(i => i.CreatedUtc).FirstOrDefault()?.SizeBytes ?? 0;
            return Math.Max(0, TotalSizeBytes - keep);
        }
    }
}
