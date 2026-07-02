using System;
using System.Collections.Generic;
using System.Linq;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;
using PrivateGalleryVault.ViewModels;

namespace PrivateGalleryVault.Controllers;

public sealed class FolderNavigationController
{
    private const int FolderPathGuardLimit = 40;
    private readonly DatabaseService _database;

    public FolderNavigationController(VaultContext context)
    {
        _database = context.Database;
    }

    public TopicFolder? GetFolder(string? folderId)
    {
        var normalizedId = NormalizeFolderId(folderId);
        return normalizedId == null ? null : _database.GetTopicFolderById(normalizedId);
    }

    public string? NormalizeFolderId(string? folderId)
    {
        return string.IsNullOrWhiteSpace(folderId) ? null : folderId.Trim();
    }

    public string BuildPathText(Topic? topic, string? currentFolderId)
    {
        if (topic == null)
            return "전체 보관함";

        var names = GetFolderLineage(currentFolderId).Select(folder => folder.Name).ToList();
        return names.Count == 0 ? $"{topic.Name} / 루트" : $"{topic.Name} / " + string.Join(" / ", names);
    }

    public IReadOnlyList<FolderBreadcrumbItemViewModel> BuildBreadcrumbItems(Topic? topic, string? currentFolderId)
    {
        var items = new List<FolderBreadcrumbItemViewModel>();
        if (topic == null)
            return items;

        var lineage = GetFolderLineage(currentFolderId);
        items.Add(new FolderBreadcrumbItemViewModel(topic.Name, null, lineage.Count > 0, "주제 루트로 이동"));

        for (var i = 0; i < lineage.Count; i++)
        {
            var folder = lineage[i];
            items.Add(new FolderBreadcrumbItemViewModel(folder.Name, folder.Id, i < lineage.Count - 1, $"'{folder.Name}' 폴더로 이동"));
        }

        return items;
    }

    public List<TopicFolder> GetFolderLineage(string? folderId)
    {
        var lineage = new List<TopicFolder>();
        var currentId = NormalizeFolderId(folderId);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guard = 0;

        while (!string.IsNullOrWhiteSpace(currentId) && guard++ < FolderPathGuardLimit)
        {
            if (!visited.Add(currentId))
            {
                AppLogger.Warn($"Folder lineage cycle detected. folderId={currentId}");
                break;
            }

            var folder = _database.GetTopicFolderById(currentId);
            if (folder == null)
                break;

            lineage.Insert(0, folder);
            currentId = NormalizeFolderId(folder.ParentFolderId);
        }

        return lineage;
    }

    public bool TryValidateCurrentFolder(Topic? topic, string? currentFolderId, out string? normalizedFolderId, out string? warning)
    {
        normalizedFolderId = NormalizeFolderId(currentFolderId);
        warning = null;

        if (topic == null)
        {
            normalizedFolderId = null;
            return true;
        }

        if (normalizedFolderId == null)
            return true;

        var folder = _database.GetTopicFolderById(normalizedFolderId);
        if (folder == null)
        {
            warning = $"Current folder not found. Reset to root. topicId={topic.Id}; missingFolderId={normalizedFolderId}";
            normalizedFolderId = null;
            return false;
        }

        if (!string.Equals(folder.TopicId, topic.Id, StringComparison.OrdinalIgnoreCase))
        {
            warning = $"Current folder belongs to another topic. Reset to root. selectedTopicId={topic.Id}; folderId={folder.Id}; folderTopicId={folder.TopicId}";
            normalizedFolderId = null;
            return false;
        }

        return true;
    }

    public string? GetParentFolderId(string? folderId)
    {
        return GetFolder(folderId)?.ParentFolderId;
    }

    public string GetParentFolderName(string? folderId)
    {
        var parentId = GetParentFolderId(folderId);
        return string.IsNullOrWhiteSpace(parentId) ? "루트" : (_database.GetTopicFolderById(parentId)?.Name ?? "상위 폴더");
    }

    public string GetFolderDisplayName(string? folderId, string rootName = "루트")
    {
        var normalizedId = NormalizeFolderId(folderId);
        return normalizedId == null ? rootName : (_database.GetTopicFolderById(normalizedId)?.Name ?? "대상 폴더");
    }

    public string GetUniqueFolderName(string topicId, string? parentFolderId, string requestedName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "새 폴더" : requestedName.Trim();
        var normalizedParent = NormalizeFolderId(parentFolderId);
        var existingNames = _database.GetTopicFolders(topicId, normalizedParent)
            .Select(folder => folder.Name?.Trim() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        if (!existingNames.Contains(baseName))
            return baseName;

        for (var number = 2; number < 10000; number++)
        {
            var candidate = $"{baseName}({number})";
            if (!existingNames.Contains(candidate))
                return candidate;
        }

        return $"{baseName}({DateTime.Now:HHmmss})";
    }

    public bool CanMoveTopicFolderToParent(Topic? selectedTopic, bool isOrderEditMode, string? sourceFolderId, string? targetParentFolderId, out string statusText)
    {
        statusText = string.Empty;
        if (selectedTopic == null || isOrderEditMode)
        {
            statusText = isOrderEditMode ? "순서 편집 중에는 폴더를 이동할 수 없습니다." : "먼저 주제를 선택하세요.";
            return false;
        }

        var normalizedSourceId = NormalizeFolderId(sourceFolderId);
        if (normalizedSourceId == null)
        {
            statusText = "이동할 폴더를 확인할 수 없습니다.";
            return false;
        }

        var sourceFolder = _database.GetTopicFolderById(normalizedSourceId);
        if (sourceFolder == null)
        {
            statusText = "이동할 폴더를 찾을 수 없습니다.";
            return false;
        }

        if (!string.Equals(sourceFolder.TopicId, selectedTopic.Id, StringComparison.OrdinalIgnoreCase))
        {
            statusText = "다른 주제의 폴더는 여기로 이동할 수 없습니다.";
            return false;
        }

        var normalizedTarget = NormalizeFolderId(targetParentFolderId);
        if (string.Equals(sourceFolder.Id, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            statusText = "폴더를 자기 자신 안으로 이동할 수 없습니다.";
            return false;
        }

        var currentParent = NormalizeFolderId(sourceFolder.ParentFolderId);
        if (string.Equals(currentParent, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            statusText = $"'{sourceFolder.Name}' 폴더는 이미 이 위치에 있습니다.";
            return false;
        }

        if (normalizedTarget != null)
        {
            var targetFolder = _database.GetTopicFolderById(normalizedTarget);
            if (targetFolder == null || !string.Equals(targetFolder.TopicId, sourceFolder.TopicId, StringComparison.OrdinalIgnoreCase))
            {
                statusText = "이 주제에 속하지 않는 위치로는 이동할 수 없습니다.";
                return false;
            }

            var descendants = _database.GetTopicFolderDescendantIds(sourceFolder.Id, includeSelf: false);
            if (descendants.Any(id => string.Equals(id, normalizedTarget, StringComparison.OrdinalIgnoreCase)))
            {
                statusText = "폴더를 자신의 하위 폴더 안으로 이동할 수 없습니다.";
                return false;
            }
        }

        var duplicate = _database.GetTopicFolders(sourceFolder.TopicId, normalizedTarget)
            .Any(folder => !string.Equals(folder.Id, sourceFolder.Id, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals((folder.Name ?? string.Empty).Trim(), (sourceFolder.Name ?? string.Empty).Trim(), StringComparison.CurrentCultureIgnoreCase));
        if (duplicate)
        {
            statusText = "같은 위치에 같은 이름의 폴더가 있어 이동할 수 없습니다.";
            return false;
        }

        var targetName = GetFolderDisplayName(normalizedTarget);
        statusText = $"놓으면 '{sourceFolder.Name}' 폴더를 '{targetName}' 아래로 이동합니다.";
        return true;
    }
}
