using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Views;

public partial class ActivityLogView : UserControl
{
    private readonly VaultContext _context;
    private string? _filter;
    private readonly ObservableCollection<ActivityRow> _rows = [];

    public ActivityLogView(VaultContext context)
    {
        InitializeComponent();
        _context = context;
        ActivityList.ItemsSource = _rows;
        LoadLogs();
    }

    private void LoadLogs()
    {
        _rows.Clear();
        var logs = _context.ActivityLogs.GetRecent(500, string.IsNullOrWhiteSpace(_filter) ? null : _filter);
        foreach (var log in logs)
            _rows.Add(new ActivityRow(log));

        var today = DateTime.Today;
        var todayLogs = logs.Where(l => l.CreatedUtc.ToLocalTime().Date == today).ToList();
        TodayCountText.Text = todayLogs.Count.ToString();
        SuccessCountText.Text = logs.Count(l => l.Result.Equals("success", StringComparison.OrdinalIgnoreCase)).ToString();
        FailCountText.Text = logs.Count(l => !l.Result.Equals("success", StringComparison.OrdinalIgnoreCase)).ToString();
        SummaryList.ItemsSource = logs.GroupBy(l => NormalizeActionName(l.ActionType)).Select(g => new { Name = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToList();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadLogs();

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _filter = (sender as Button)?.Tag?.ToString();
        LoadLogs();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "작업 기록 내보내기",
            FileName = $"PrivateGalleryVault_Activity_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            Filter = "CSV 파일|*.csv|텍스트 파일|*.txt"
        };
        if (dlg.ShowDialog() != true)
            return;

        var lines = new List<string> { "Time,Action,Title,Detail,Result,Actor" };
        foreach (var row in _rows)
            lines.Add($"\"{row.TimeText}\",\"{row.ActionType}\",\"{row.Title}\",\"{row.Detail}\",\"{row.Result}\",\"{row.Actor}\"");
        File.WriteAllLines(dlg.FileName, lines);
        _context.ActivityLogs.Add("export", "작업 기록 내보내기", Path.GetFileName(dlg.FileName));
        LoadLogs();
    }

    private static string NormalizeActionName(string action) => action switch
    {
        "import" => "가져오기",
        "export" => "내보내기",
        "duplicate" => "중복 처리",
        "topic" => "주제 변경",
        "tag" => "태그",
        "backup" => "백업",
        "restore" => "복원",
        _ => "기타"
    };

    public sealed class ActivityRow
    {
        public string ActionType { get; }
        public string Title { get; }
        public string Detail { get; }
        public string Result { get; }
        public string Actor { get; }
        public string TimeText { get; }
        public string Icon { get; }
        public Brush AccentBrush { get; }

        public ActivityRow(ActivityLog log)
        {
            ActionType = log.ActionType;
            Title = log.Title;
            Detail = log.Detail;
            Result = log.Result;
            Actor = log.Actor;
            TimeText = log.CreatedUtc.ToLocalTime().ToString("MM.dd HH:mm");
            Icon = log.ActionType switch
            {
                "import" => "↥",
                "export" => "↧",
                "duplicate" => "≋",
                "topic" => "✎",
                "tag" => "●",
                "backup" => "▣",
                "restore" => "↺",
                _ => "i"
            };
            AccentBrush = new SolidColorBrush(log.ActionType switch
            {
                "import" => Color.FromRgb(37, 99, 235),
                "export" => Color.FromRgb(14, 165, 233),
                "duplicate" => Color.FromRgb(245, 158, 11),
                "topic" => Color.FromRgb(168, 85, 247),
                "tag" => Color.FromRgb(34, 197, 94),
                "backup" => Color.FromRgb(59, 130, 246),
                "restore" => Color.FromRgb(16, 185, 129),
                _ => Color.FromRgb(51, 65, 85)
            });
        }
    }
}
