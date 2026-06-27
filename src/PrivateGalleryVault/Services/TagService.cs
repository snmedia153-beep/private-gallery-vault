using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

public sealed class TagService
{
    private readonly DatabaseService _database;

    private static readonly (string Name, string Color)[] DefaultTags =
    [
        ("업무", "#3B82F6"),
        ("개인", "#22C55E"),
        ("중요", "#EF4444"),
        ("참고", "#A855F7"),
        ("영상", "#06B6D4"),
        ("문서", "#60A5FA"),
        ("보관", "#F59E0B"),
        ("긴급", "#F97316")
    ];

    public TagService(DatabaseService database)
    {
        _database = database;
    }

    public List<Tag> GetTags()
    {
        EnsureDefaultTags();
        return _database.GetTags();
    }

    public Tag CreateTag(string name, string color) => _database.CreateTag(name, color);
    public void UpdateTag(string tagId, string name, string color) => _database.UpdateTag(tagId, name, color);
    public void DeleteTag(string tagId) => _database.DeleteTag(tagId);
    public void Apply(string tagId, IEnumerable<string> mediaIds) => _database.ApplyTagToMedia(tagId, mediaIds);
    public void Remove(string tagId, IEnumerable<string> mediaIds) => _database.RemoveTagFromMedia(tagId, mediaIds);
    public List<Tag> GetTagsForMedia(string mediaId) => _database.GetTagsForMedia(mediaId);

    private void EnsureDefaultTags()
    {
        if (_database.GetTags().Count > 0)
            return;

        foreach (var (name, color) in DefaultTags)
            _database.CreateTag(name, color);
    }
}
