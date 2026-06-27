using System.Windows.Media;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.ViewModels;

public sealed class TopicCardViewModel
{
    public Topic Topic { get; }
    public ImageSource CoverImage { get; }
    public string Id => Topic.Id;
    public string Name => Topic.Name;
    public string Description => string.IsNullOrWhiteSpace(Topic.Description) ? "설명 없음" : Topic.Description;
    public string CountText => $"{Topic.ItemCount}개 항목";
    public string UpdatedText => Topic.UpdatedUtc.ToLocalTime().ToString("yyyy.MM.dd HH:mm");

    public TopicCardViewModel(Topic topic, ImageSource coverImage)
    {
        Topic = topic;
        CoverImage = coverImage;
    }
}
