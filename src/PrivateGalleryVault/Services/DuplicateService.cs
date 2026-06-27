using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

public sealed class DuplicateService
{
    private readonly DatabaseService _database;

    public DuplicateService(DatabaseService database)
    {
        _database = database;
    }

    public List<DuplicateGroup> GetGroups(int limit = 100) => _database.GetDuplicateGroups(limit);
}
