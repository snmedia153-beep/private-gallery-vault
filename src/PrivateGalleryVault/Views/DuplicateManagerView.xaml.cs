using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Views;

public partial class DuplicateManagerView : UserControl
{
    private readonly VaultContext _context;
    private readonly ObservableCollection<GroupRow> _groups = [];
    private readonly ObservableCollection<PreviewRow> _previews = [];

    public DuplicateManagerView(VaultContext context)
    {
        InitializeComponent();
        _context = context;
        GroupList.ItemsSource = _groups;
        PreviewList.ItemsSource = _previews;
        LoadGroups();
    }

    private void LoadGroups()
    {
        _groups.Clear();
        foreach (var group in _context.Duplicates.GetGroups())
            _groups.Add(new GroupRow(group));
        if (_groups.Count > 0)
            GroupList.SelectedIndex = 0;
    }

    private void Refresh_Click(object sender, System.Windows.RoutedEventArgs e) => LoadGroups();

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _previews.Clear();
        if (GroupList.SelectedItem is not GroupRow row)
            return;

        GroupTitleText.Text = $"그룹 #{GroupList.SelectedIndex + 1}";
        GroupHashText.Text = $"SHA-256 지문: {row.Group.SourceHash[..Math.Min(16, row.Group.SourceHash.Length)]}... · {row.Group.Count}개 파일";
        WastedSizeText.Text = FormatBytes(row.Group.EstimatedWastedBytes);
        CompareFileNameText.Text = row.Group.Items.FirstOrDefault()?.OriginalName ?? "-";

        var ordered = row.Group.Items.OrderBy(i => i.CreatedUtc).ToList();
        for (var i = 0; i < ordered.Count; i++)
            _previews.Add(new PreviewRow(ordered[i], i == 0, LoadThumbnailSafe(ordered[i])));
    }


    private void DeleteDuplicates_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (GroupList.SelectedItem is not GroupRow row || row.Group.Items.Count <= 1)
            return;

        var keep = row.Group.Items.OrderBy(i => i.CreatedUtc).First();
        var deleteTargets = row.Group.Items.Where(i => i.Id != keep.Id).ToList();
        var confirm = System.Windows.MessageBox.Show($"원본 '{keep.OriginalName}'은 유지하고 중복 파일 {deleteTargets.Count}개를 삭제할까요?", "중복 파일 삭제", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        foreach (var item in deleteTargets)
            _context.Media.DeleteMediaFiles(item);

        _context.ActivityLogs.Add("duplicate", "중복 파일 삭제", $"원본 유지: {keep.OriginalName} · 삭제 {deleteTargets.Count}개");
        LoadGroups();
    }

    private BitmapImage? LoadThumbnailSafe(MediaItem item)
    {
        try { return _context.Media.LoadThumbnail(item); }
        catch { return null; }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    public sealed class GroupRow
    {
        public DuplicateGroup Group { get; }
        public string Title => $"중복 그룹 ({Group.Count})";
        public string Subtitle => $"절약 가능 {FormatBytes(Group.EstimatedWastedBytes)}";
        public int Count => Group.Count;
        public GroupRow(DuplicateGroup group) { Group = group; }
    }

    public sealed class PreviewRow
    {
        public string Role { get; }
        public Brush RoleBrush { get; }
        public string Name { get; }
        public string Detail { get; }
        public BitmapImage? Thumbnail { get; }

        public PreviewRow(MediaItem item, bool isOriginal, BitmapImage? thumbnail)
        {
            Role = isOriginal ? "원본 유지 권장" : "중복 파일";
            RoleBrush = new SolidColorBrush(isOriginal ? Color.FromRgb(96, 165, 250) : Color.FromRgb(248, 113, 113));
            Name = item.OriginalName;
            Detail = $"{item.CreatedUtc.ToLocalTime():yyyy.MM.dd HH:mm} · {FormatBytes(item.SizeBytes)}";
            Thumbnail = thumbnail;
        }
    }
}
