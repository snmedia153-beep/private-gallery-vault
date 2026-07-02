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

    private static readonly Dictionary<string, string> PaletteNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#3B82F6"] = "파랑",
        ["#22C55E"] = "초록",
        ["#EF4444"] = "빨강",
        ["#F59E0B"] = "노랑",
        ["#A855F7"] = "보라",
        ["#06B6D4"] = "민트",
        ["#F97316"] = "주황",
        ["#EC4899"] = "핑크",
        ["#84CC16"] = "라임",
        ["#64748B"] = "회색"
    };

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
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(5),
                Padding = new Thickness(0),
                Tag = color,
                Background = ToBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderThickness = new Thickness(1),
                ToolTip = $"{GetPaletteName(color)} 라벨 적용/제거"
            };
            button.Click += PaletteColor_Click;
            PaletteGrid.Children.Add(button);
        }
    }

    private void LoadAll(IEnumerable<string>? mediaIdsToSelect = null)
    {
        _tags.Clear();
        foreach (var tag in _context.Tags.GetTags())
            _tags.Add(new TagRow(tag));

        if (_tags.Count > 0 && TagList.SelectedIndex < 0)
            TagList.SelectedIndex = 0;

        LoadMedia();
        RestoreMediaSelection(mediaIdsToSelect);
        UpdatePaletteStatusForSelection();
    }

    private void LoadMedia()
    {
        _media.Clear();
        foreach (var item in _context.Database.GetMedia().Take(120))
            _media.Add(new MediaRow(item, LoadThumbnailSafe(item), _context.Tags.GetTagsForMedia(item.Id)));
    }

    private void RestoreMediaSelection(IEnumerable<string>? mediaIdsToSelect)
    {
        if (mediaIdsToSelect == null)
            return;

        var ids = mediaIdsToSelect.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return;

        MediaList.SelectedItems.Clear();
        foreach (var item in MediaList.Items.OfType<MediaRow>())
        {
            if (ids.Contains(item.Id))
                MediaList.SelectedItems.Add(item);
        }
    }

    private BitmapImage? LoadThumbnailSafe(MediaItem item)
    {
        try { return _context.Media.LoadThumbnail(item); }
        catch { return null; }
    }

    private void TagList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagList.SelectedItem is TagRow tag)
        {
            _selectedColor = tag.Color;
            PaletteStatusText.Text = $"새 태그 색상: {tag.Name}. 파일 선택 후 팔레트 색상을 클릭하면 즉시 적용/해제됩니다.";
        }
    }

    private void MediaList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePaletteStatusForSelection();

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadAll(GetSelectedMediaRows().Select(m => m.Id));

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = $"새 태그 {_tags.Count + 1}";
        _context.Tags.CreateTag(name, _selectedColor);
        _context.ActivityLogs.Add("tag", "태그 생성", name);
        LoadAll(GetSelectedMediaRows().Select(m => m.Id));
    }

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color })
            return;

        _selectedColor = color;
        ToggleColorLabelForSelectedMedia(color);
    }

    private void ToggleColorLabelForSelectedMedia(string color)
    {
        var selectedRows = GetSelectedMediaRows();
        if (selectedRows.Count == 0)
        {
            PaletteStatusText.Text = $"{GetPaletteName(color)} 색상을 선택했습니다. 파일을 먼저 선택하면 클릭 즉시 적용/해제됩니다.";
            return;
        }

        var tag = GetOrCreateTagForColor(color);
        var selectedIds = selectedRows.Select(row => row.Id).ToList();
        var allSelectedAlreadyHaveColor = selectedRows.All(row => row.HasTag(tag.Id));

        string statusMessage;
        if (allSelectedAlreadyHaveColor)
        {
            _context.Tags.Remove(tag.Id, selectedIds);
            _context.ActivityLogs.Add("tag", "태그 제거", $"{tag.Name} · {selectedIds.Count}개 파일");
            statusMessage = $"{tag.Name} 라벨을 {selectedIds.Count}개 파일에서 제거했습니다.";
        }
        else
        {
            _context.Tags.Apply(tag.Id, selectedIds);
            _context.ActivityLogs.Add("tag", "태그 적용", $"{tag.Name} · {selectedIds.Count}개 파일");
            statusMessage = $"{tag.Name} 라벨을 {selectedIds.Count}개 파일에 적용했습니다.";
        }

        LoadAll(selectedIds);
        PaletteStatusText.Text = statusMessage;
    }

    private Tag GetOrCreateTagForColor(string color)
    {
        var tag = _context.Tags.GetTags().FirstOrDefault(t => string.Equals(t.Color, color, StringComparison.OrdinalIgnoreCase));
        if (tag != null)
            return tag;

        var name = GetPaletteName(color);
        tag = _context.Tags.CreateTag(name, color);
        _context.ActivityLogs.Add("tag", "태그 생성", name);
        return tag;
    }

    private List<MediaRow> GetSelectedMediaRows()
    {
        return MediaList.SelectedItems.Cast<MediaRow>().ToList();
    }

    private void UpdatePaletteStatusForSelection()
    {
        if (PaletteStatusText == null || MediaList == null)
            return;

        var count = MediaList.SelectedItems.Count;
        PaletteStatusText.Text = count == 0
            ? "파일을 선택한 뒤 색상을 클릭하세요. 선택된 파일에 바로 적용/해제됩니다."
            : $"선택된 파일 {count}개. 색상을 클릭하면 즉시 적용/해제됩니다.";
    }

    private static string GetPaletteName(string color)
    {
        return PaletteNames.TryGetValue(color, out var name) ? name : $"색상 {color}";
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
        public string Color { get; }
        public string CountText { get; }
        public Brush Brush { get; }

        public TagRow(Tag tag)
        {
            Id = tag.Id;
            Name = tag.Name;
            Color = tag.Color;
            CountText = tag.ItemCount.ToString();
            Brush = ToBrush(tag.Color);
        }
    }

    public sealed class MediaTagChip
    {
        public string Id { get; }
        public string Name { get; }
        public string Color { get; }
        public Brush Brush { get; }

        public MediaTagChip(Tag tag)
        {
            Id = tag.Id;
            Name = tag.Name;
            Color = tag.Color;
            Brush = ToBrush(tag.Color);
        }
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
            Id = item.Id;
            Title = item.OriginalName;
            Thumbnail = thumbnail;
            Badge = item.Kind switch { MediaKind.Video => "VIDEO", MediaKind.Document => "DOC", MediaKind.Archive => "ZIP", _ => "FILE" };
            Detail = $"{item.CreatedUtc.ToLocalTime():yyyy.MM.dd} · {FormatBytes(item.SizeBytes)}";
            Tags = tags.Select(t => new MediaTagChip(t)).ToList();
        }

        public bool HasTag(string tagId)
        {
            return Tags.Any(tag => string.Equals(tag.Id, tagId, StringComparison.OrdinalIgnoreCase));
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
