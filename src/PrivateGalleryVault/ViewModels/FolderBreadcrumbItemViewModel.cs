namespace PrivateGalleryVault.ViewModels;

public sealed class FolderBreadcrumbItemViewModel
{
    public string Name { get; }
    public string? FolderId { get; }
    public string SeparatorText { get; }
    public string ToolTip { get; }

    public FolderBreadcrumbItemViewModel(string name, string? folderId, bool showSeparator, string toolTip)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "루트" : name.Trim();
        FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
        SeparatorText = showSeparator ? " /" : string.Empty;
        ToolTip = string.IsNullOrWhiteSpace(toolTip) ? Name : toolTip;
    }
}
