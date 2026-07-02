using System;
using System.Collections.Generic;
using System.Linq;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Controllers;

/// <summary>
/// 미디어 목록 검색/필터/범위 계산을 MainWindow에서 분리한 컨트롤러입니다.
/// UI 상태는 MainWindow가 보유하고, 이 클래스는 DB 조회와 순수 필터 판단만 담당합니다.
/// </summary>
public sealed class MediaFilterController
{
    public const string ScopeCurrent = "current";
    public const string ScopeDescendants = "descendants";
    public const string ScopeTopic = "topic";

    private readonly VaultContext _context;

    public MediaFilterController(VaultContext context)
    {
        _context = context;
    }

    public static string NormalizeFolderSearchScope(string? scope)
    {
        return scope switch
        {
            ScopeDescendants => ScopeDescendants,
            ScopeTopic => ScopeTopic,
            _ => ScopeCurrent
        };
    }

    public static string GetFolderSearchScopeLabel(string? scope)
    {
        return NormalizeFolderSearchScope(scope) switch
        {
            ScopeDescendants => "하위 포함",
            ScopeTopic => "주제 전체",
            _ => "현재 폴더"
        };
    }

    public List<MediaItem> GetMediaForFolderScope(Topic topic, string? currentFolderId, string? scope, MediaKind? kind)
    {
        return NormalizeFolderSearchScope(scope) switch
        {
            ScopeDescendants => _context.Database.GetMediaInFolder(topic.Id, currentFolderId, kind, includeDescendants: true),
            ScopeTopic => _context.Database.GetMediaInFolder(topic.Id, null, kind, includeDescendants: true),
            _ => _context.Database.GetMediaInFolder(topic.Id, currentFolderId, kind, includeDescendants: false)
        };
    }

    public List<MediaItem> BuildFilteredItems(MediaFilterQuery query)
    {
        List<MediaItem> items = query.SelectedTopic != null
            ? GetMediaForFolderScope(query.SelectedTopic, query.CurrentFolderId, query.FolderSearchScope, query.KindFilter)
            : _context.Database.GetMedia(null, query.KindFilter);

        if (query.FavoritesOnly)
            items = items.Where(item => item.Favorite).ToList();

        if (query.IncludeTagFilter && !string.IsNullOrWhiteSpace(query.SelectedMediaTagFilterId))
        {
            var tagId = query.SelectedMediaTagFilterId.Trim();
            items = items
                .Where(item => _context.Tags.GetTagsForMedia(item.Id)
                    .Any(tag => string.Equals(tag.Id, tagId, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var normalizedSearch = query.SearchText?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var topicNameById = query.Topics
                .GroupBy(topic => topic.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

            items = items.Where(item => MediaMatchesSearch(item, normalizedSearch, topicNameById)).ToList();
        }

        return items;
    }

    public Dictionary<string, int> BuildTagFilterCounts(MediaFilterQuery query)
    {
        var countQuery = query with { IncludeTagFilter = false };
        var countBaseItems = BuildFilteredItems(countQuery);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in countBaseItems)
        {
            foreach (var tag in _context.Tags.GetTagsForMedia(item.Id))
            {
                if (string.IsNullOrWhiteSpace(tag.Id))
                    continue;

                counts[tag.Id] = counts.TryGetValue(tag.Id, out var current) ? current + 1 : 1;
            }
        }

        return counts;
    }

    public bool ShouldShowMediaLocationText(MediaItem? item, Topic? selectedTopic, string? folderSearchScope)
    {
        if (item == null)
            return false;

        if (selectedTopic == null)
            return true;

        if (!string.Equals(item.TopicId, selectedTopic.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(NormalizeFolderSearchScope(folderSearchScope), ScopeCurrent, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public int CountByKind(IEnumerable<MediaItem> items, MediaKind kind)
    {
        return items.Count(item => item.Kind == kind);
    }

    private static bool MediaMatchesSearch(MediaItem item, string normalizedSearch, IReadOnlyDictionary<string, string> topicNameById)
    {
        if (item.OriginalName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            return true;

        if (item.Extension.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            return true;

        return topicNameById.TryGetValue(item.TopicId, out var topicName)
               && topicName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record MediaFilterQuery(
    Topic? SelectedTopic,
    string? CurrentFolderId,
    string? FolderSearchScope,
    MediaKind? KindFilter,
    bool FavoritesOnly,
    string? SelectedMediaTagFilterId,
    string? SearchText,
    IReadOnlyCollection<Topic> Topics,
    bool IncludeTagFilter = true);
