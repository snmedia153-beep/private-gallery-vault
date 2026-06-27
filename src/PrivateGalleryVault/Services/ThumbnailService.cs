using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

public static class ThumbnailService
{
    private const int DefaultThumbnailSize = 480;
    private const int DefaultFrameLongEdge = 1280;

    public static byte[] CreateThumbnailBytes(string sourcePath, MediaKind kind, int size = DefaultThumbnailSize)
    {
        if (kind == MediaKind.Image)
            return CreateImageThumbnailBytes(sourcePath, size);

        if (kind == MediaKind.Video)
        {
            return TryCreateVideoThumbnailSet(sourcePath, size, out var videoThumb, out _, out _)
                ? videoThumb
                : CreateVideoPlaceholderBytes(Path.GetFileName(sourcePath), size);
        }

        return CreateFilePlaceholderBytes(KindLabel(kind, Path.GetExtension(sourcePath)), size, kind);
    }

    public static bool TryCreateVideoThumbnailBytes(string sourcePath, int size, out byte[] bytes)
    {
        return TryCreateVideoThumbnailSet(sourcePath, size, out bytes, out _, out _);
    }

    public static bool TryCreateVideoThumbnailSet(
        string sourcePath,
        int size,
        out byte[] thumbnailBytes,
        out byte[] sourceFrameBytes,
        out TimeSpan capturedAt)
    {
        thumbnailBytes = Array.Empty<byte>();
        sourceFrameBytes = Array.Empty<byte>();
        capturedAt = TimeSpan.Zero;

        if (!File.Exists(sourcePath))
            return false;

        if (!TryCaptureVideoFrameSourceBytes(sourcePath, null, DefaultFrameLongEdge, out sourceFrameBytes, out capturedAt))
            return false;

        try
        {
            var frame = BytesToBitmap(sourceFrameBytes);
            thumbnailBytes = CreateSquareThumbnailBytes(frame, size);
            return thumbnailBytes.Length > 0;
        }
        catch
        {
            thumbnailBytes = Array.Empty<byte>();
            sourceFrameBytes = Array.Empty<byte>();
            return false;
        }
    }

    public static bool TryCaptureVideoFrameSourceBytes(
        string sourcePath,
        TimeSpan? preferredPosition,
        int maxLongEdge,
        out byte[] sourceFrameBytes,
        out TimeSpan capturedAt)
    {
        sourceFrameBytes = Array.Empty<byte>();
        capturedAt = TimeSpan.Zero;

        if (!File.Exists(sourcePath))
            return false;

        if (TryCaptureVideoFrameWithMediaPlayer(sourcePath, preferredPosition, maxLongEdge, out sourceFrameBytes, out capturedAt))
            return true;

        // 일부 코덱은 WPF MediaPlayer 캡처가 실패할 수 있습니다.
        // 이 경우에만 Windows Shell 썸네일 공급자를 폴백으로 사용합니다.
        return TryCaptureVideoFrameWithShell(sourcePath, maxLongEdge, out sourceFrameBytes);
    }

    public static BitmapImage BytesToBitmap(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public static byte[] CreateSquareThumbnailBytes(BitmapSource src, int size = DefaultThumbnailSize)
    {
        if (src.PixelWidth <= 0 || src.PixelHeight <= 0)
            throw new ArgumentException("썸네일 소스 이미지 크기가 올바르지 않습니다.", nameof(src));

        var scale = Math.Max((double)size / src.PixelWidth, (double)size / src.PixelHeight);
        var width = Math.Max(1, src.PixelWidth * scale);
        var height = Math.Max(1, src.PixelHeight * scale);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(5, 11, 20)), null, new Rect(0, 0, size, size));
            dc.DrawImage(src, new Rect((size - width) / 2.0, (size - height) / 2.0, width, height));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        return EncodeBitmap(rtb, usePng: false, jpegQuality: 90);
    }

    public static BitmapImage CreatePlaceholderBitmap(string label = "PRIVATE")
    {
        return BytesToBitmap(CreateFilePlaceholderBytes(label, DefaultThumbnailSize, MediaKind.Other));
    }

    public static BitmapImage CreatePlaceholderBitmap(MediaKind kind, string? extension = null)
    {
        return BytesToBitmap(CreatePlaceholderBytes(kind, extension));
    }

    public static byte[] CreatePlaceholderBytes(MediaKind kind, string? extension = null, int size = DefaultThumbnailSize)
    {
        return kind == MediaKind.Video
            ? CreateVideoPlaceholderBytes("VIDEO", size)
            : CreateFilePlaceholderBytes(KindLabel(kind, extension), size, kind);
    }

    public static bool TryReadImageDimensions(string sourcePath, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            frame.Freeze();
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            return width > 0 && height > 0;
        }
        catch
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    private static byte[] CreateImageThumbnailBytes(string sourcePath, int size)
    {
        // UriSource는 파일명에 깨진 문자(�), 특수문자, 일부 비표준 유니코드가 섞인 경우
        // WIC/URI 변환 단계에서 실패할 수 있습니다. 파일 스트림으로 직접 읽으면
        // 파일명 인코딩 영향을 받지 않고 실제 바이트만 디코딩하므로 가져오기가 더 안정적입니다.
        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var src = new BitmapImage();
        src.BeginInit();
        src.StreamSource = stream;
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        src.DecodePixelWidth = Math.Max(size, 1) * 2;
        src.EndInit();
        src.Freeze();

        return CreateSquareThumbnailBytes(src, size);
    }

    private static bool TryCaptureVideoFrameWithMediaPlayer(
        string sourcePath,
        TimeSpan? preferredPosition,
        int maxLongEdge,
        out byte[] sourceFrameBytes,
        out TimeSpan capturedAt)
    {
        sourceFrameBytes = Array.Empty<byte>();
        capturedAt = TimeSpan.Zero;

        MediaPlayer? player = null;
        var opened = false;
        var failed = false;

        try
        {
            player = new MediaPlayer
            {
                Volume = 0,
                ScrubbingEnabled = true
            };
            player.MediaOpened += (_, _) => opened = true;
            player.MediaFailed += (_, _) => failed = true;

            player.Open(new Uri(sourcePath, UriKind.Absolute));
            if (!WaitUntil(() => opened || failed, 5000) || failed)
                return false;

            var naturalWidth = player.NaturalVideoWidth;
            var naturalHeight = player.NaturalVideoHeight;
            if (naturalWidth <= 0 || naturalHeight <= 0)
                return false;

            var duration = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.Zero;
            capturedAt = ResolveCapturePosition(duration, preferredPosition);

            player.Position = capturedAt;
            player.Play();
            DoEventsFor(650);
            player.Pause();
            DoEventsFor(120);

            naturalWidth = player.NaturalVideoWidth;
            naturalHeight = player.NaturalVideoHeight;
            if (naturalWidth <= 0 || naturalHeight <= 0)
                return false;

            var scale = Math.Min(1.0, (double)Math.Max(1, maxLongEdge) / Math.Max(naturalWidth, naturalHeight));
            var width = Math.Max(1, (int)Math.Round(naturalWidth * scale));
            var height = Math.Max(1, (int)Math.Round(naturalHeight * scale));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                dc.DrawVideo(player, new Rect(0, 0, width, height));
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            sourceFrameBytes = EncodeBitmap(rtb, usePng: false, jpegQuality: 92);
            return sourceFrameBytes.Length > 0;
        }
        catch
        {
            sourceFrameBytes = Array.Empty<byte>();
            capturedAt = TimeSpan.Zero;
            return false;
        }
        finally
        {
            try
            {
                player?.Stop();
                player?.Close();
            }
            catch
            {
            }
        }
    }

    private static TimeSpan ResolveCapturePosition(TimeSpan duration, TimeSpan? preferredPosition)
    {
        if (preferredPosition.HasValue)
        {
            if (duration > TimeSpan.Zero)
            {
                var max = duration > TimeSpan.FromMilliseconds(350)
                    ? duration - TimeSpan.FromMilliseconds(300)
                    : duration;
                if (preferredPosition.Value < TimeSpan.Zero) return TimeSpan.Zero;
                if (preferredPosition.Value > max) return max;
            }
            return preferredPosition.Value < TimeSpan.Zero ? TimeSpan.Zero : preferredPosition.Value;
        }

        if (duration <= TimeSpan.Zero)
            return TimeSpan.FromSeconds(2);

        if (duration <= TimeSpan.FromSeconds(1))
            return TimeSpan.FromMilliseconds(Math.Max(80, duration.TotalMilliseconds * 0.35));

        var candidate = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.10);
        if (candidate < TimeSpan.FromSeconds(2) && duration > TimeSpan.FromSeconds(3))
            candidate = TimeSpan.FromSeconds(2);

        var upper = duration - TimeSpan.FromMilliseconds(500);
        if (upper > TimeSpan.Zero && candidate > upper)
            candidate = upper;

        return candidate < TimeSpan.Zero ? TimeSpan.Zero : candidate;
    }

    private static bool TryCaptureVideoFrameWithShell(string sourcePath, int maxLongEdge, out byte[] sourceFrameBytes)
    {
        sourceFrameBytes = Array.Empty<byte>();

        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(sourcePath, IntPtr.Zero, ref iid, out var factory);

            var nativeSize = new SIZE { cx = Math.Max(1, maxLongEdge), cy = Math.Max(1, maxLongEdge) };
            factory.GetImage(nativeSize, SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_RESIZETOFIT | SIIGBF.SIIGBF_THUMBNAILONLY, out var hBitmap);
            if (hBitmap == IntPtr.Zero)
                return false;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();

                sourceFrameBytes = EncodeBitmap(source, usePng: false, jpegQuality: 92);
                return sourceFrameBytes.Length > 0;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            sourceFrameBytes = Array.Empty<byte>();
            return false;
        }
    }

    private static bool WaitUntil(Func<bool> predicate, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate() && DateTime.UtcNow < deadline)
            DoEventsFor(40);
        return predicate();
    }

    private static void DoEventsFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds))
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static byte[] CreateVideoPlaceholderBytes(string name, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(5, 11, 20)), null, new Rect(0, 0, size, size), 24, 24);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 42)), new Pen(new SolidColorBrush(Color.FromRgb(38, 54, 77)), 2), new Rect(28, 62, size - 56, size - 124), 18, 18);

            var triangle = new StreamGeometry();
            using (var geo = triangle.Open())
            {
                geo.BeginFigure(new Point(size / 2.0 - 34, size / 2.0 - 48), true, true);
                geo.LineTo(new Point(size / 2.0 - 34, size / 2.0 + 48), true, false);
                geo.LineTo(new Point(size / 2.0 + 54, size / 2.0), true, false);
            }
            triangle.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(96, 165, 250)), null, triangle);

            DrawCenteredLabel(dc, "VIDEO", size, size - 88, 32, Color.FromRgb(229, 231, 235));
        }

        return RenderVisualToPngBytes(visual, size);
    }

    private static byte[] CreateFilePlaceholderBytes(string label, int size, MediaKind kind)
    {
        var accent = kind switch
        {
            MediaKind.Document => Color.FromRgb(96, 165, 250),
            MediaKind.Archive => Color.FromRgb(251, 191, 36),
            MediaKind.Other => Color.FromRgb(167, 139, 250),
            _ => Color.FromRgb(96, 165, 250)
        };
        var accentBrush = new SolidColorBrush(accent);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(5, 11, 20)), null, new Rect(0, 0, size, size), 24, 24);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(12, 22, 36)), new Pen(new SolidColorBrush(Color.FromRgb(38, 54, 77)), 2), new Rect(54, 54, size - 108, size - 108), 24, 24);

            var docRect = new Rect(size * 0.29, size * 0.20, size * 0.42, size * 0.54);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 42)), new Pen(accentBrush, 3), docRect, 12, 12);
            var fold = new StreamGeometry();
            using (var geo = fold.Open())
            {
                geo.BeginFigure(new Point(docRect.Right - 84, docRect.Top), true, true);
                geo.LineTo(new Point(docRect.Right, docRect.Top + 84), true, false);
                geo.LineTo(new Point(docRect.Right, docRect.Top), true, false);
            }
            fold.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)), new Pen(accentBrush, 2), fold);

            if (kind == MediaKind.Archive)
            {
                for (var i = 0; i < 5; i++)
                {
                    var y = docRect.Top + 52 + i * 34;
                    dc.DrawRectangle(i % 2 == 0 ? accentBrush : new SolidColorBrush(Color.FromRgb(203, 213, 225)), null, new Rect(docRect.Left + 28, y, 36, 22));
                }
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    var y = docRect.Top + 100 + i * 34;
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(71, 85, 105)), null, new Rect(docRect.Left + 34, y, docRect.Width - 68, 9), 4, 4);
                }
            }

            DrawCenteredLabel(dc, label, size, size - 100, 34, accent);
        }
        return RenderVisualToPngBytes(visual, size);
    }

    private static void DrawCenteredLabel(DrawingContext dc, string textValue, int size, double y, double fontSize, Color color)
    {
        var text = new FormattedText(
            textValue,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            fontSize,
            new SolidColorBrush(color),
            1.0);
        dc.DrawText(text, new Point((size - text.Width) / 2, y));
    }

    private static byte[] RenderVisualToPngBytes(DrawingVisual visual, int size)
    {
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return EncodeBitmap(rtb, usePng: true, jpegQuality: 90);
    }

    private static byte[] EncodeBitmap(BitmapSource source, bool usePng, int jpegQuality)
    {
        BitmapEncoder encoder = usePng
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static string KindLabel(MediaKind kind, string? extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.TrimStart('.').ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5)
            return ext;
        return kind switch
        {
            MediaKind.Document => "DOC",
            MediaKind.Archive => "ZIP",
            MediaKind.Other => "FILE",
            MediaKind.Video => "VIDEO",
            _ => "IMAGE"
        };
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00000000,
        SIIGBF_BIGGERSIZEOK = 0x00000001,
        SIIGBF_THUMBNAILONLY = 0x00000008,
        SIIGBF_SCALEUP = 0x00000010
    }
}
