using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Windows;

public partial class VideoThumbnailWindow : Window
{
    private readonly string _videoPath;
    private readonly DispatcherTimer _timer;
    private bool _isDraggingSlider;
    private bool _isPlaying;
    private readonly bool _playVideosMuted;
    private TimeSpan _duration = TimeSpan.Zero;

    public byte[]? SourceFrameBytes { get; private set; }
    public byte[]? ThumbnailBytes { get; private set; }
    public TimeSpan CapturePosition { get; private set; }

    public VideoThumbnailWindow(string videoPath, string title)
    {
        InitializeComponent();
        _videoPath = videoPath;
        _playVideosMuted = AppSettingsService.Load().PlayVideosMuted;
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

        ApplyMutePreference();
        PreviewPlayer.Source = new Uri(_videoPath, UriKind.Absolute);
        _timer.Start();
    }

    private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        _duration = PreviewPlayer.NaturalDuration.HasTimeSpan ? PreviewPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
        TimelineSlider.Maximum = _duration.TotalSeconds > 0 ? _duration.TotalSeconds : 1;
        TimelineSlider.Value = 0;
        PreviewPlayer.Position = TimeSpan.Zero;
        ApplyMutePreference();
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
            ApplyMutePreference();
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

    private async void CaptureAndCrop_Click(object sender, RoutedEventArgs e)
    {
        var wasPlaying = _isPlaying;
        PreviewPlayer.Pause();
        ApplyMutePreference();
        _isPlaying = false;
        PlayPauseButton.Content = "재생";

        var requestedPosition = PreviewPlayer.Position;
        try
        {
            byte[] frameBytes;
            TimeSpan capturedAt;

            // Use the source video frame first. Capturing the MediaElement visual can include
            // black letterbox/pillarbox areas or a stale rendered frame on some Windows codecs.
            // The source-frame path keeps the crop window based on the real frame ratio.
            if (!ThumbnailService.TryCaptureVideoFrameSourceBytes(_videoPath, requestedPosition, 1280, out frameBytes, out capturedAt))
            {
                await PrepareCurrentPreviewFrameAsync(requestedPosition);
                capturedAt = PreviewPlayer.Position;

                if (!TryCaptureCurrentPreviewFrameBytes(out frameBytes))
                {
                    MessageDialog.Show(this, "현재 프레임을 캡처할 수 없습니다. 다른 시점으로 이동하거나 Windows 코덱 지원 여부를 확인하세요.",
                        "영상 썸네일", MessageBoxButton.OK, MessageBoxImage.Warning);
                    RestorePlaybackIfNeeded(wasPlaying);
                    return;
                }
            }

            var source = ThumbnailService.BytesToBitmap(frameBytes);
            var crop = new ThumbnailCropWindow(source, $"영상 프레임 {FormatTime(capturedAt)}") { Owner = this };
            if (crop.ShowDialog() != true || crop.ThumbnailBytes == null)
            {
                RestorePlaybackIfNeeded(wasPlaying);
                return;
            }

            SourceFrameBytes = frameBytes;
            ThumbnailBytes = crop.ThumbnailBytes;
            CapturePosition = capturedAt;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "영상 프레임 크롭 중 오류가 발생했습니다.\n\n" + ex.Message, "영상 썸네일", MessageBoxButton.OK, MessageBoxImage.Error);
            RestorePlaybackIfNeeded(wasPlaying);
        }
    }

    private async Task PrepareCurrentPreviewFrameAsync(TimeSpan requestedPosition)
    {
        // MediaElement는 위치 이동 직후 이전 프레임을 잠깐 유지할 수 있습니다.
        // 짧게 재생 후 다시 일시정지하여 화면에 보이는 프레임과 저장 대상 프레임을 맞춥니다.
        PreviewPlayer.Position = requestedPosition;
        await Dispatcher.Yield(DispatcherPriority.Render);
        await Task.Delay(80);

        ApplyMutePreference();
        PreviewPlayer.Play();
        await Task.Delay(90);
        PreviewPlayer.Pause();
        await Task.Delay(140);
        await Dispatcher.InvokeAsync(() => PreviewPlayer.UpdateLayout(), DispatcherPriority.Render);

        UpdateTimelineFromPlayer();
    }

    private bool TryCaptureCurrentPreviewFrameBytes(out byte[] frameBytes)
    {
        frameBytes = Array.Empty<byte>();

        var width = Math.Max(1, (int)Math.Round(PreviewPlayer.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(PreviewPlayer.ActualHeight));
        if (width <= 1 || height <= 1)
            return false;

        try
        {
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(PreviewPlayer);
            rtb.Freeze();

            var frame = ExtractDisplayedVideoContent(rtb);
            frame = TrimNearBlackBorders(frame);
            frame.Freeze();

            if (IsMostlyBlank(frame))
                return false;

            var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            frameBytes = ms.ToArray();
            return frameBytes.Length > 0;
        }
        catch
        {
            frameBytes = Array.Empty<byte>();
            return false;
        }
    }

    private BitmapSource ExtractDisplayedVideoContent(BitmapSource renderedPlayer)
    {
        var naturalWidth = PreviewPlayer.NaturalVideoWidth;
        var naturalHeight = PreviewPlayer.NaturalVideoHeight;
        if (naturalWidth <= 0 || naturalHeight <= 0)
            return renderedPlayer;

        var renderWidth = renderedPlayer.PixelWidth;
        var renderHeight = renderedPlayer.PixelHeight;
        if (renderWidth <= 1 || renderHeight <= 1)
            return renderedPlayer;

        var scale = Math.Min((double)renderWidth / naturalWidth, (double)renderHeight / naturalHeight);
        var contentWidth = Math.Max(1, (int)Math.Round(naturalWidth * scale));
        var contentHeight = Math.Max(1, (int)Math.Round(naturalHeight * scale));
        var contentX = Math.Clamp((renderWidth - contentWidth) / 2, 0, Math.Max(0, renderWidth - 1));
        var contentY = Math.Clamp((renderHeight - contentHeight) / 2, 0, Math.Max(0, renderHeight - 1));
        contentWidth = Math.Min(contentWidth, renderWidth - contentX);
        contentHeight = Math.Min(contentHeight, renderHeight - contentY);

        if (contentWidth <= 1 || contentHeight <= 1)
            return renderedPlayer;

        if (contentX == 0 && contentY == 0 && contentWidth == renderWidth && contentHeight == renderHeight)
            return renderedPlayer;

        var cropped = new CroppedBitmap(renderedPlayer, new Int32Rect(contentX, contentY, contentWidth, contentHeight));
        cropped.Freeze();
        return cropped;
    }

    private static BitmapSource TrimNearBlackBorders(BitmapSource source)
    {
        try
        {
            var bitmap = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            bitmap.Freeze();

            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            if (width < 40 || height < 40)
                return source;

            var stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            var left = 0;
            var right = width - 1;
            var top = 0;
            var bottom = height - 1;

            while (left < right - 16 && IsDarkColumn(pixels, stride, height, left))
                left++;
            while (right > left + 16 && IsDarkColumn(pixels, stride, height, right))
                right--;
            while (top < bottom - 16 && IsDarkRow(pixels, stride, width, top))
                top++;
            while (bottom > top + 16 && IsDarkRow(pixels, stride, width, bottom))
                bottom--;

            var trimmedWidth = right - left + 1;
            var trimmedHeight = bottom - top + 1;
            if (trimmedWidth < width * 0.30 || trimmedHeight < height * 0.30)
                return source;

            if (left <= 2 && top <= 2 && width - right <= 3 && height - bottom <= 3)
                return source;

            var cropped = new CroppedBitmap(bitmap, new Int32Rect(left, top, trimmedWidth, trimmedHeight));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return source;
        }
    }

    private static bool IsDarkColumn(byte[] pixels, int stride, int height, int x)
    {
        var dark = 0;
        for (var y = 0; y < height; y += Math.Max(1, height / 240))
        {
            var i = y * stride + x * 4;
            if (IsDarkPixel(pixels[i], pixels[i + 1], pixels[i + 2]))
                dark++;
        }
        var sampled = Math.Max(1, (height + Math.Max(1, height / 240) - 1) / Math.Max(1, height / 240));
        return dark >= sampled * 0.985;
    }

    private static bool IsDarkRow(byte[] pixels, int stride, int width, int y)
    {
        var step = Math.Max(1, width / 240);
        var dark = 0;
        var sampled = 0;
        for (var x = 0; x < width; x += step)
        {
            var i = y * stride + x * 4;
            if (IsDarkPixel(pixels[i], pixels[i + 1], pixels[i + 2]))
                dark++;
            sampled++;
        }
        return sampled == 0 || dark >= sampled * 0.985;
    }

    private static bool IsDarkPixel(byte b, byte g, byte r) => r <= 14 && g <= 14 && b <= 14;

    private static bool IsMostlyBlank(BitmapSource bitmap)
    {
        try
        {
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);

            var step = Math.Max(4, pixels.Length / 2400 / 4 * 4);
            var meaningful = 0;
            var sampled = 0;
            for (var i = 0; i + 2 < pixels.Length; i += step)
            {
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                if (r > 12 || g > 12 || b > 12)
                    meaningful++;
                sampled++;
            }

            return sampled == 0 || meaningful < sampled * 0.02;
        }
        catch
        {
            return false;
        }
    }

    private void RestorePlaybackIfNeeded(bool wasPlaying)
    {
        if (!wasPlaying)
            return;

        ApplyMutePreference();
        PreviewPlayer.Play();
        _isPlaying = true;
        PlayPauseButton.Content = "일시정지";
    }


    private void ApplyMutePreference()
    {
        try
        {
            PreviewPlayer.IsMuted = _playVideosMuted;
            PreviewPlayer.Volume = _playVideosMuted ? 0 : 0.65;
        }
        catch
        {
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
            PreviewPlayer.IsMuted = true;
            PreviewPlayer.Volume = 0;
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
