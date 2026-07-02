using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrivateGalleryVault.Windows;

public partial class ThumbnailCropWindow : Window
{
    private const int FrameSize = 480;
    private readonly BitmapSource _source;

    // Crop state is stored in source-pixel coordinates instead of visual-control coordinates.
    // This keeps image/video thumbnail cropping consistent across landscape, portrait, square,
    // and ultra-wide sources.
    private double _baseScale = 1.0;
    private double _zoom = 1.0;
    private double _centerX;
    private double _centerY;
    private double _imageLeft;
    private double _imageTop;
    private double _displayWidth;
    private double _displayHeight;

    private bool _isDragging;
    private bool _isUpdatingZoomSlider;
    private Point _dragStart;
    private double _dragStartCenterX;
    private double _dragStartCenterY;

    public byte[]? ThumbnailBytes { get; private set; }

    public ThumbnailCropWindow(BitmapSource source, string title)
    {
        InitializeComponent();
        _source = source;
        HeaderText.Text = string.IsNullOrWhiteSpace(title) ? "썸네일 크롭" : $"썸네일 크롭 · {title}";
        Loaded += ThumbnailCropWindow_Loaded;
    }

    public static BitmapSource LoadBitmapFromFile(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void ThumbnailCropWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PreviewImage.Source = _source;
        ResetCrop();
    }

    private void ResetCrop()
    {
        if (_source.PixelWidth <= 0 || _source.PixelHeight <= 0)
            return;

        _baseScale = Math.Max((double)FrameSize / _source.PixelWidth, (double)FrameSize / _source.PixelHeight);
        _zoom = 1.0;
        _centerX = _source.PixelWidth / 2.0;
        _centerY = _source.PixelHeight / 2.0;
        ClampSourceCenter();
        ApplyPreviewPlacement();
        SyncZoomSlider();
    }

    private double CurrentScale => Math.Max(0.0001, _baseScale * _zoom);

    private void SetZoom(double value, Point anchorInFrame, bool keepAnchor = true)
    {
        if (_source.PixelWidth <= 0 || _source.PixelHeight <= 0)
            return;

        var oldScale = CurrentScale;
        var sourceAnchorX = _centerX + (anchorInFrame.X - FrameSize / 2.0) / oldScale;
        var sourceAnchorY = _centerY + (anchorInFrame.Y - FrameSize / 2.0) / oldScale;

        _zoom = Math.Clamp(value, ZoomSlider.Minimum, ZoomSlider.Maximum);
        var newScale = CurrentScale;

        if (keepAnchor)
        {
            _centerX = sourceAnchorX - (anchorInFrame.X - FrameSize / 2.0) / newScale;
            _centerY = sourceAnchorY - (anchorInFrame.Y - FrameSize / 2.0) / newScale;
        }

        ClampSourceCenter();
        ApplyPreviewPlacement();
        SyncZoomSlider();
    }

    private void ApplyPreviewPlacement()
    {
        var scale = CurrentScale;
        _displayWidth = _source.PixelWidth * scale;
        _displayHeight = _source.PixelHeight * scale;
        _imageLeft = FrameSize / 2.0 - _centerX * scale;
        _imageTop = FrameSize / 2.0 - _centerY * scale;

        PreviewImage.Width = Math.Max(1, _displayWidth);
        PreviewImage.Height = Math.Max(1, _displayHeight);
        Canvas.SetLeft(PreviewImage, _imageLeft);
        Canvas.SetTop(PreviewImage, _imageTop);
    }

    private void ClampSourceCenter()
    {
        var scale = CurrentScale;
        var visibleSourceWidth = Math.Min(_source.PixelWidth, FrameSize / scale);
        var visibleSourceHeight = Math.Min(_source.PixelHeight, FrameSize / scale);

        if (visibleSourceWidth >= _source.PixelWidth - 0.0001)
            _centerX = _source.PixelWidth / 2.0;
        else
            _centerX = Math.Clamp(_centerX, visibleSourceWidth / 2.0, _source.PixelWidth - visibleSourceWidth / 2.0);

        if (visibleSourceHeight >= _source.PixelHeight - 0.0001)
            _centerY = _source.PixelHeight / 2.0;
        else
            _centerY = Math.Clamp(_centerY, visibleSourceHeight / 2.0, _source.PixelHeight - visibleSourceHeight / 2.0);
    }

    private void SyncZoomSlider()
    {
        if (Math.Abs(ZoomSlider.Value - _zoom) <= 0.0001)
            return;

        _isUpdatingZoomSlider = true;
        ZoomSlider.Value = _zoom;
        _isUpdatingZoomSlider = false;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _isUpdatingZoomSlider)
            return;

        SetZoom(e.NewValue, new Point(FrameSize / 2.0, FrameSize / 2.0));
    }

    private void CropFrame_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var next = _zoom * (e.Delta > 0 ? 1.10 : 1 / 1.10);
        SetZoom(next, e.GetPosition(CropFrame));
        e.Handled = true;
    }

    private void CropFrame_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(CropFrame);
        _dragStartCenterX = _centerX;
        _dragStartCenterY = _centerY;
        CropFrame.Cursor = Cursors.ScrollAll;
        CropFrame.CaptureMouse();
        e.Handled = true;
    }

    private void CropFrame_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
            return;

        var current = e.GetPosition(CropFrame);
        var scale = CurrentScale;

        // Dragging the visual image to the right reveals the left side of the source, so the
        // source center moves in the opposite direction of the pointer delta.
        _centerX = _dragStartCenterX - (current.X - _dragStart.X) / scale;
        _centerY = _dragStartCenterY - (current.Y - _dragStart.Y) / scale;
        ClampSourceCenter();
        ApplyPreviewPlacement();
        e.Handled = true;
    }

    private void CropFrame_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void CropFrame_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging && e.LeftButton != MouseButtonState.Pressed)
            EndDrag();
    }

    private void EndDrag()
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        CropFrame.ReleaseMouseCapture();
        CropFrame.Cursor = Cursors.Hand;
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => ResetCrop();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ThumbnailBytes = RenderCroppedThumbnailBytes();
        DialogResult = true;
    }

    private byte[] RenderCroppedThumbnailBytes()
    {
        var scale = CurrentScale;
        var cropSourceWidth = Math.Min(_source.PixelWidth, FrameSize / scale);
        var cropSourceHeight = Math.Min(_source.PixelHeight, FrameSize / scale);

        var cropX = _centerX - cropSourceWidth / 2.0;
        var cropY = _centerY - cropSourceHeight / 2.0;
        cropX = Math.Clamp(cropX, 0, Math.Max(0, _source.PixelWidth - cropSourceWidth));
        cropY = Math.Clamp(cropY, 0, Math.Max(0, _source.PixelHeight - cropSourceHeight));

        var rect = ToSafeSourceRect(cropX, cropY, cropSourceWidth, cropSourceHeight);
        var cropped = new CroppedBitmap(_source, rect);
        cropped.Freeze();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(5, 11, 20)), null, new Rect(0, 0, FrameSize, FrameSize));
            dc.DrawImage(cropped, new Rect(0, 0, FrameSize, FrameSize));
        }

        var rtb = new RenderTargetBitmap(FrameSize, FrameSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private Int32Rect ToSafeSourceRect(double x, double y, double width, double height)
    {
        var left = Math.Clamp((int)Math.Floor(x), 0, Math.Max(0, _source.PixelWidth - 1));
        var top = Math.Clamp((int)Math.Floor(y), 0, Math.Max(0, _source.PixelHeight - 1));
        var right = Math.Clamp((int)Math.Ceiling(x + width), left + 1, _source.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(y + height), top + 1, _source.PixelHeight);
        return new Int32Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }
}
