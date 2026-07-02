using System;
using System.Collections.Generic;
using System.Linq;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;
using PrivateGalleryVault.ViewModels;

namespace PrivateGalleryVault.Controllers;

public sealed class OrderEditSnapshot
{
    public List<string> TopicIds { get; init; } = [];
    public List<string> MediaIds { get; init; } = [];
    public List<string> FolderIds { get; init; } = [];
}

public sealed class OrderEditRestoreResult
{
    public bool TopicChanged { get; init; }
    public bool MediaChanged { get; init; }
    public bool FolderChanged { get; init; }
    public bool AnyChanged => TopicChanged || MediaChanged || FolderChanged;
}

public sealed class OrderEditController
{
    private readonly VaultContext _context;

    public OrderEditController(VaultContext context)
    {
        _context = context;
    }

    public OrderEditSnapshot CaptureSnapshot(
        IEnumerable<string> topicIds,
        IEnumerable<string> mediaIds,
        IEnumerable<string> folderIds)
    {
        return new OrderEditSnapshot
        {
            TopicIds = NormalizeIds(topicIds),
            MediaIds = NormalizeIds(mediaIds),
            FolderIds = NormalizeIds(folderIds)
        };
    }

    public OrderEditRestoreResult RestoreSnapshot(
        IReadOnlyList<string>? topicSnapshot,
        IReadOnlyList<string>? mediaSnapshot,
        IReadOnlyList<string>? folderSnapshot,
        IEnumerable<string> currentTopicIds,
        IEnumerable<string> currentMediaIds,
        IEnumerable<string> currentFolderIds)
    {
        var topicChanged = HasOrderChanged(topicSnapshot, currentTopicIds);
        var mediaChanged = HasOrderChanged(mediaSnapshot, currentMediaIds);
        var folderChanged = HasOrderChanged(folderSnapshot, currentFolderIds);

        if (topicChanged && topicSnapshot is { Count: > 0 })
            _context.Database.UpdateTopicSortOrders(topicSnapshot);

        if (mediaChanged && mediaSnapshot is { Count: > 0 })
            _context.Database.UpdateMediaSortOrders(mediaSnapshot);

        if (folderChanged && folderSnapshot is { Count: > 0 })
            _context.Database.UpdateTopicFolderSortOrders(folderSnapshot);

        return new OrderEditRestoreResult
        {
            TopicChanged = topicChanged,
            MediaChanged = mediaChanged,
            FolderChanged = folderChanged
        };
    }

    public List<string> BuildDefaultTopicOrder(IEnumerable<Topic> topics, Func<Topic, bool> isFixedTopic)
    {
        ArgumentNullException.ThrowIfNull(isFixedTopic);

        return topics
            .Where(topic => !string.IsNullOrWhiteSpace(topic.Id))
            .OrderBy(topic => isFixedTopic(topic) ? 0 : 1)
            .ThenBy(topic => topic.CreatedUtc)
            .ThenBy(topic => topic.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(topic => topic.Id)
            .ToList();
    }

    public List<string> BuildDefaultFolderOrder(IEnumerable<TopicFolder> folders)
    {
        return folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
            .OrderBy(folder => folder.CreatedUtc)
            .ThenBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(folder => folder.Id)
            .ToList();
    }

    public List<string> BuildDefaultMediaOrder(IEnumerable<MediaItem> mediaItems)
    {
        return mediaItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .OrderByDescending(item => item.CreatedUtc)
            .ThenBy(item => item.OriginalName, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Id)
            .ToList();
    }

    public bool IsFixedTopic(object? item, Func<Topic, bool> isFixedTopic, Func<TopicCardViewModel, bool> isFixedTopicCard)
    {
        return item switch
        {
            Topic topic => isFixedTopic(topic),
            TopicCardViewModel topicCard => isFixedTopicCard(topicCard),
            _ => false
        };
    }

    public static bool HasOrderChanged(IReadOnlyList<string>? snapshot, IEnumerable<string> currentIds)
    {
        if (snapshot == null || snapshot.Count == 0)
            return false;

        var current = NormalizeIds(currentIds);
        if (snapshot.Count != current.Count)
            return true;

        for (var i = 0; i < snapshot.Count; i++)
        {
            if (!string.Equals(snapshot[i], current[i], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static List<string> NormalizeIds(IEnumerable<string> ids)
    {
        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();
    }
}
