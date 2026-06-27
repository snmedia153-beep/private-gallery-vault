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
    private double _baseScale = 1.0;
    private double _zoom = 1.0;
    private double _offsetX;
    private double _offsetY;
    private bool _isDragging;
    private Point _dragStart;
    private double _dragStartX;
    private double _dragStartY;

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
        _offsetX = 0;
        _offsetY = 0;
        ZoomSlider.Value = 1.0;
        UpdateImagePlacement();
    }

    private void UpdateImagePlacement()
    {
        var scale = _baseScale * _zoom;
        var width = Math.Max(1, _source.PixelWidth * scale);
        var height = Math.Max(1, _source.PixelHeight * scale);

        ClampOffsets(width, height);

        PreviewImage.Width = width;
        PreviewImage.Height = height;
        Canvas.SetLeft(PreviewImage, (FrameSize - width) / 2.0 + _offsetX);
        Canvas.SetTop(PreviewImage, (FrameSize - height) / 2.0 + _offsetY);
    }

    private void ClampOffsets(double displayWidth, double displayHeight)
    {
        var maxX = Math.Max(0, (displayWidth - FrameSize) / 2.0);
        var maxY = Math.Max(0, (displayHeight - FrameSize) / 2.0);
        _offsetX = Math.Clamp(_offsetX, -maxX, maxX);
        _offsetY = Math.Clamp(_offsetY, -maxY, maxY);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        _zoom = Math.Clamp(e.NewValue, 1.0, 5.0);
        UpdateImagePlacement();
    }

    private void CropFrame_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var next = ZoomSlider.Value * (e.Delta > 0 ? 1.10 : 1 / 1.10);
        ZoomSlider.Value = Math.Clamp(next, ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    private void CropFrame_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(CropFrame);
        _dragStartX = _offsetX;
        _dragStartY = _offsetY;
        CropFrame.Cursor = Cursors.ScrollAll;
        CropFrame.CaptureMouse();
        e.Handled = true;
    }

    private void CropFrame_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
            return;

        var current = e.GetPosition(CropFrame);
        _offsetX = _dragStartX + current.X - _dragStart.X;
        _offsetY = _dragStartY + current.Y - _dragStart.Y;
        UpdateImagePlacement();
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
        var scale = _baseScale * _zoom;
        var displayWidth = _source.PixelWidth * scale;
        var displayHeight = _source.PixelHeight * scale;
        var imageLeft = (FrameSize - displayWidth) / 2.0 + _offsetX;
        var imageTop = (FrameSize - displayHeight) / 2.0 + _offsetY;

        var cropX = Math.Max(0, -imageLeft / scale);
        var cropY = Math.Max(0, -imageTop / scale);
        var cropW = Math.Min(_source.PixelWidth - cropX, FrameSize / scale);
        var cropH = Math.Min(_source.PixelHeight - cropY, FrameSize / scale);

        var rect = new Int32Rect(
            Math.Clamp((int)Math.Round(cropX), 0, Math.Max(0, _source.PixelWidth - 1)),
            Math.Clamp((int)Math.Round(cropY), 0, Math.Max(0, _source.PixelHeight - 1)),
            Math.Max(1, Math.Min((int)Math.Round(cropW), _source.PixelWidth)),
            Math.Max(1, Math.Min((int)Math.Round(cropH), _source.PixelHeight)));

        if (rect.X + rect.Width > _source.PixelWidth)
            rect.Width = _source.PixelWidth - rect.X;
        if (rect.Y + rect.Height > _source.PixelHeight)
            rect.Height = _source.PixelHeight - rect.Y;

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

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
