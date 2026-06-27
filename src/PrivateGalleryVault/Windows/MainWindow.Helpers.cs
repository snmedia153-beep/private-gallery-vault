using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PrivateGalleryVault.Windows;

internal enum ReorderDropPlacement
{
    Before,
    After
}

internal enum ReorderSurfaceVisualKind
{
    List,
    Grid
}

internal sealed class ReorderDropCueAdorner : Adorner
{
    private static readonly Brush OverlayBrush = CreateBrush(32, 59, 130, 246);
    private static readonly Pen OutlinePen = CreatePen(110, 96, 165, 250, 1.2);
    private static readonly Pen GlowPen = CreatePen(70, 96, 165, 250, 6);
    private static readonly Pen LinePen = CreatePen(255, 96, 165, 250, 3);
    private static readonly Brush DotBrush = CreateBrush(255, 191, 219, 254);

    public ReorderDropPlacement Placement { get; }
    public ReorderSurfaceVisualKind SurfaceKind { get; }

    public ReorderDropCueAdorner(UIElement adornedElement, ReorderDropPlacement placement, ReorderSurfaceVisualKind surfaceKind)
        : base(adornedElement)
    {
        Placement = placement;
        SurfaceKind = surfaceKind;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var rect = new Rect(1.5, 1.5, Math.Max(0, AdornedElement.RenderSize.Width - 3), Math.Max(0, AdornedElement.RenderSize.Height - 3));
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var corner = SurfaceKind == ReorderSurfaceVisualKind.List ? 14 : 16;
        dc.DrawRoundedRectangle(OverlayBrush, OutlinePen, rect, corner, corner);

        if (SurfaceKind == ReorderSurfaceVisualKind.List)
        {
            var y = Placement == ReorderDropPlacement.Before ? rect.Top + 2 : rect.Bottom - 2;
            DrawLine(dc, new Point(rect.Left + 12, y), new Point(rect.Right - 12, y));
        }
        else
        {
            var x = Placement == ReorderDropPlacement.Before ? rect.Left + 2 : rect.Right - 2;
            DrawLine(dc, new Point(x, rect.Top + 12), new Point(x, rect.Bottom - 12));
        }
    }

    private static void DrawLine(DrawingContext dc, Point start, Point end)
    {
        dc.DrawLine(GlowPen, start, end);
        dc.DrawLine(LinePen, start, end);
        dc.DrawEllipse(DotBrush, null, start, 4.5, 4.5);
        dc.DrawEllipse(DotBrush, null, end, 4.5, 4.5);
    }

    private static SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(byte a, byte r, byte g, byte b, double thickness)
    {
        var pen = new Pen(CreateBrush(a, r, g, b), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }
}

public partial class MainWindow
{
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
