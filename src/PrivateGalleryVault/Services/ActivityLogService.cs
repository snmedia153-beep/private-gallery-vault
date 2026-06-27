using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

public sealed class ActivityLogService
{
    private readonly DatabaseService _database;

    public ActivityLogService(DatabaseService database)
    {
        _database = database;
    }

    public void Add(string actionType, string title, string detail = "", string targetId = "", string targetName = "", string result = "success", string actor = "User")
    {
        _database.AddActivityLog(actionType, title, detail, targetId, targetName, result, actor);
    }

    public List<ActivityLog> GetRecent(int limit = 300, string? actionType = null) => _database.GetActivityLogs(limit, actionType);
}
