using System;
using System.Collections.Generic;
using System.Linq;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.ViewModels;

namespace PrivateGalleryVault.Controllers;

public sealed class SidebarTopicController
{
    public const string SortCustom = "custom";
    public const string SortLatest = "latest";
    public const string SortNameDesc = "nameDesc";
    public const string SortNameAsc = "nameAsc";

    public string NormalizeSortMode(string? mode)
    {
        return mode switch
        {
            SortLatest => SortLatest,
            SortNameDesc => SortNameDesc,
            SortNameAsc => SortNameAsc,
            _ => SortCustom
        };
    }

    public string GetSortLabel(string? mode)
    {
        return NormalizeSortMode(mode) switch
        {
            SortLatest => "최신 등록 순",
            SortNameDesc => "이름 내림차순",
            SortNameAsc => "이름 오름차순",
            _ => "사용자 설정 순서"
        };
    }

    public IReadOnlyList<Topic> ApplySearchAndSort(IEnumerable<Topic> topics, string? query, string? sortMode)
    {
        return SortTopics(topics.Where(topic => TopicMatchesSearch(topic, query)), sortMode).ToList();
    }

    public IEnumerable<Topic> SortTopics(IEnumerable<Topic> topics, string? sortMode)
    {
        var topicList = topics.ToList();
        var fixedTopics = topicList
            .Where(IsFixedUncategorizedTopic)
            .OrderBy(topic => topic.CreatedUtc)
            .ThenBy(topic => topic.Id, StringComparer.OrdinalIgnoreCase);

        IEnumerable<Topic> normalTopics = topicList.Where(topic => !IsFixedUncategorizedTopic(topic));
        normalTopics = NormalizeSortMode(sortMode) switch
        {
            SortLatest => normalTopics.OrderByDescending(topic => topic.CreatedUtc),
            SortNameDesc => normalTopics.OrderByDescending(topic => topic.Name, StringComparer.CurrentCultureIgnoreCase),
            SortNameAsc => normalTopics.OrderBy(topic => topic.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => normalTopics
        };

        return fixedTopics.Concat(normalTopics);
    }

    public bool TopicMatchesSearch(Topic topic, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var normalizedQuery = query.Trim();
        return ContainsIgnoreCase(topic.Name, normalizedQuery)
            || ContainsIgnoreCase(topic.Description, normalizedQuery)
            || ContainsIgnoreCase(topic.ItemCount.ToString(), normalizedQuery);
    }

    public string BuildEmptySearchMessage(string? query)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? "검색어" : query.Trim();
        return $"'{normalizedQuery}'와 일치하는 주제 폴더가 없습니다. 검색어를 지우거나 새 주제를 추가해 주세요.";
    }

    public static bool IsFixedUncategorizedTopic(Topic? topic)
    {
        return topic != null
               && string.Equals(topic.Name?.Trim(), "미분류", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFixedUncategorizedTopic(TopicCardViewModel? topic)
    {
        return topic != null
               && string.Equals(topic.Name?.Trim(), "미분류", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnoreCase(string? source, string query)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
