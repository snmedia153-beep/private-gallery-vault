using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Views;

public partial class TagManagerView : UserControl
{
    private readonly VaultContext _context;
    private readonly ObservableCollection<TagRow> _tags = [];
    private readonly ObservableCollection<MediaRow> _media = [];
    private string _selectedColor = "#3B82F6";

    private static readonly string[] Palette = ["#3B82F6", "#22C55E", "#EF4444", "#F59E0B", "#A855F7", "#06B6D4", "#F97316", "#EC4899", "#84CC16", "#64748B"];

    public TagManagerView(VaultContext context)
    {
        InitializeComponent();
        _context = context;
        TagList.ItemsSource = _tags;
        MediaList.ItemsSource = _media;
        BuildPalette();
        LoadAll();
    }

    private void BuildPalette()
    {
        PaletteGrid.Children.Clear();
        foreach (var color in Palette)
        {
            var button = new Button { Width = 28, Height = 28, Margin = new Thickness(4), Padding = new Thickness(0), Tag = color, Background = ToBrush(color), BorderBrush = Brushes.Transparent };
            button.Click += (_, _) => _selectedColor = color;
            PaletteGrid.Children.Add(button);
        }
    }

    private void LoadAll()
    {
        _tags.Clear();
        foreach (var tag in _context.Tags.GetTags())
            _tags.Add(new TagRow(tag));
        if (_tags.Count > 0 && TagList.SelectedIndex < 0)
            TagList.SelectedIndex = 0;
        LoadMedia();
    }

    private void LoadMedia()
    {
        _media.Clear();
        foreach (var item in _context.Database.GetMedia().Take(120))
            _media.Add(new MediaRow(item, LoadThumbnailSafe(item), _context.Tags.GetTagsForMedia(item.Id)));
    }

    private BitmapImage? LoadThumbnailSafe(MediaItem item)
    {
        try { return _context.Media.LoadThumbnail(item); }
        catch { return null; }
    }

    private void TagList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedTagText.Text = TagList.SelectedItem is TagRow tag ? $"선택 태그: {tag.Name}" : "선택 태그: 없음";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadAll();

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = $"새 태그 {_tags.Count + 1}";
        _context.Tags.CreateTag(name, _selectedColor);
        _context.ActivityLogs.Add("tag", "태그 생성", name);
        LoadAll();
    }

    private void ApplyTag_Click(object sender, RoutedEventArgs e)
    {
        if (TagList.SelectedItem is not TagRow tag)
            return;
        var ids = MediaList.SelectedItems.Cast<MediaRow>().Select(m => m.Id).ToList();
        if (ids.Count == 0)
            return;
        _context.Tags.Apply(tag.Id, ids);
        _context.ActivityLogs.Add("tag", "태그 적용", $"{tag.Name} · {ids.Count}개 파일");
        LoadAll();
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (TagList.SelectedItem is not TagRow tag)
            return;
        var ids = MediaList.SelectedItems.Cast<MediaRow>().Select(m => m.Id).ToList();
        if (ids.Count == 0)
            return;
        _context.Tags.Remove(tag.Id, ids);
        _context.ActivityLogs.Add("tag", "태그 제거", $"{tag.Name} · {ids.Count}개 파일");
        LoadAll();
    }

    private static Brush ToBrush(string color)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { return new SolidColorBrush(Color.FromRgb(59, 130, 246)); }
    }

    public sealed class TagRow
    {
        public string Id { get; }
        public string Name { get; }
        public string CountText { get; }
        public Brush Brush { get; }
        public TagRow(Tag tag)
        {
            Id = tag.Id; Name = tag.Name; CountText = tag.ItemCount.ToString(); Brush = ToBrush(tag.Color);
        }
    }

    public sealed class MediaTagChip
    {
        public string Name { get; }
        public Brush Brush { get; }
        public MediaTagChip(Tag tag) { Name = tag.Name; Brush = ToBrush(tag.Color); }
    }

    public sealed class MediaRow
    {
        public string Id { get; }
        public string Title { get; }
        public string Badge { get; }
        public string Detail { get; }
        public BitmapImage? Thumbnail { get; }
        public List<MediaTagChip> Tags { get; }

        public MediaRow(MediaItem item, BitmapImage? thumbnail, List<Tag> tags)
        {
            Id = item.Id; Title = item.OriginalName; Thumbnail = thumbnail;
            Badge = item.Kind switch { MediaKind.Video => "VIDEO", MediaKind.Document => "DOC", MediaKind.Archive => "ZIP", _ => "FILE" };
            Detail = $"{item.CreatedUtc.ToLocalTime():yyyy.MM.dd} · {FormatBytes(item.SizeBytes)}";
            Tags = tags.Select(t => new MediaTagChip(t)).ToList();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
