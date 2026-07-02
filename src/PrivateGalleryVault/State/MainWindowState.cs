namespace PrivateGalleryVault.State;

/// <summary>
/// MainWindow의 핵심 화면 상태를 한 곳에서 추적하기 위한 경량 상태 객체입니다.
/// 21단계에서는 기존 필드를 즉시 모두 제거하지 않고, 리팩터링 안정성을 위해 상태 동기화부터 시작합니다.
/// </summary>
public sealed class MainWindowState
{
    public string? SelectedTopicId { get; set; }
    public string? CurrentFolderId { get; set; }
    public string FolderSearchScope { get; set; } = "current";
    public string? SelectedMediaTagFilterId { get; set; }
    public bool FavoritesOnly { get; set; }
    public bool IsOrderEditMode { get; set; }
    public bool IsImporting { get; set; }
    public bool IsReloadingMedia { get; set; }
    public bool ReloadMediaRequestedAfterCurrent { get; set; }
    public int CurrentPage { get; set; } = 1;

    public void SetNavigation(string? selectedTopicId, string? currentFolderId, string folderSearchScope)
    {
        SelectedTopicId = string.IsNullOrWhiteSpace(selectedTopicId) ? null : selectedTopicId;
        CurrentFolderId = string.IsNullOrWhiteSpace(currentFolderId) ? null : currentFolderId;
        FolderSearchScope = string.IsNullOrWhiteSpace(folderSearchScope) ? "current" : folderSearchScope;
    }
}
