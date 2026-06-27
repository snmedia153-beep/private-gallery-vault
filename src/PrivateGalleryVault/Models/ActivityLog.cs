namespace PrivateGalleryVault.Models;

public sealed class ActivityLog
{
    public string Id { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string Result { get; set; } = "success";
    public string Actor { get; set; } = "User";
    public DateTime CreatedUtc { get; set; }
}
