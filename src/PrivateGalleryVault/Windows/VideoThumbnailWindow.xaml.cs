using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Windows;

public partial class VideoThumbnailWindow : Window
{
    private readonly string _videoPath;
    private readonly DispatcherTimer _timer;
    private bool _isDraggingSlider;
    private bool _isPlaying;
    private TimeSpan _duration = TimeSpan.Zero;

    public byte[]? SourceFrameBytes { get; private set; }
    public byte[]? ThumbnailBytes { get; private set; }
    public TimeSpan CapturePosition { get; private set; }

    public VideoThumbnailWindow(string videoPath, string title)
    {
        InitializeComponent();
        _videoPath = videoPath;
        HeaderText.Text = string.IsNullOrWhiteSpace(title) ? "영상 썸네일 재설정" : $"영상 썸네일 재설정 · {title}";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _timer.Tick += (_, _) => UpdateTimelineFromPlayer();

        Loaded += VideoThumbnailWindow_Loaded;
        Closed += VideoThumbnailWindow_Closed;
    }

    private void VideoThumbnailWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_videoPath))
        {
            MessageDialog.Show(this, "임시 영상 파일을 찾을 수 없습니다.", "영상 썸네일", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            return;
        }

        PreviewPlayer.Source = new Uri(_videoPath, UriKind.Absolute);
        _timer.Start();
    }

    private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        _duration = PreviewPlayer.NaturalDuration.HasTimeSpan ? PreviewPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
        TimelineSlider.Maximum = _duration.TotalSeconds > 0 ? _duration.TotalSeconds : 1;
        TimelineSlider.Value = 0;
        PreviewPlayer.Position = TimeSpan.Zero;
        PreviewPlayer.Pause();
        _isPlaying = false;
        PlayPauseButton.Content = "재생";
        UpdateTimelineFromPlayer();
    }

    private void PreviewPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        MessageDialog.Show(this, "영상을 불러올 수 없습니다. Windows에서 지원하지 않는 코덱일 수 있습니다.\n\n" + e.ErrorException.Message,
            "영상 썸네일", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            PreviewPlayer.Pause();
            _isPlaying = false;
            PlayPauseButton.Content = "재생";
        }
        else
        {
            PreviewPlayer.Play();
            _isPlaying = true;
            PlayPauseButton.Content = "일시정지";
        }
    }

    private void BackHalf_Click(object sender, RoutedEventArgs e) => SeekBy(TimeSpan.FromMilliseconds(-500));
    private void ForwardHalf_Click(object sender, RoutedEventArgs e) => SeekBy(TimeSpan.FromMilliseconds(500));

    private void SeekBy(TimeSpan delta)
    {
        var next = PreviewPlayer.Position + delta;
        if (next < TimeSpan.Zero)
            next = TimeSpan.Zero;
        if (_duration > TimeSpan.Zero && next > _duration)
            next = _duration;
        PreviewPlayer.Position = next;
        UpdateTimelineFromPlayer();
    }

    private void TimelineSlider_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void TimelineSlider_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        PreviewPlayer.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
        UpdateTimelineFromPlayer();
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider)
            CurrentTimeText.Text = $"{FormatTime(TimeSpan.FromSeconds(e.NewValue))} / {FormatTime(_duration)}";
    }

    private void CaptureAndCrop_Click(object sender, RoutedEventArgs e)
    {
        var wasPlaying = _isPlaying;
        PreviewPlayer.Pause();
        _isPlaying = false;
        PlayPauseButton.Content = "재생";

        var requestedPosition = PreviewPlayer.Position;
        try
        {
            if (!ThumbnailService.TryCaptureVideoFrameSourceBytes(_videoPath, requestedPosition, 1280, out var frameBytes, out var capturedAt))
            {
                MessageDialog.Show(this, "현재 프레임을 캡처할 수 없습니다. 다른 시점으로 이동하거나 Windows 코덱 지원 여부를 확인하세요.",
                    "영상 썸네일", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (wasPlaying)
                {
                    PreviewPlayer.Play();
                    _isPlaying = true;
                    PlayPauseButton.Content = "일시정지";
                }
                return;
            }

            var source = ThumbnailService.BytesToBitmap(frameBytes);
            var crop = new ThumbnailCropWindow(source, "영상 프레임") { Owner = this };
            if (crop.ShowDialog() != true || crop.ThumbnailBytes == null)
                return;

            SourceFrameBytes = frameBytes;
            ThumbnailBytes = crop.ThumbnailBytes;
            CapturePosition = capturedAt;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "영상 프레임 크롭 중 오류가 발생했습니다.\n\n" + ex.Message, "영상 썸네일", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateTimelineFromPlayer()
    {
        var position = PreviewPlayer.Position;
        if (!_isDraggingSlider && TimelineSlider.Maximum > 0)
            TimelineSlider.Value = Math.Clamp(position.TotalSeconds, TimelineSlider.Minimum, TimelineSlider.Maximum);
        CurrentTimeText.Text = $"{FormatTime(position)} / {FormatTime(_duration)}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void VideoThumbnailWindow_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        try
        {
            PreviewPlayer.Stop();
            PreviewPlayer.Source = null;
        }
        catch
        {
        }
    }

    private static string FormatTime(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero)
            return "00:00";
        return ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
    }
}
