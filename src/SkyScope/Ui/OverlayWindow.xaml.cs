using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SkyScope.Core;
using SkyScope.Native;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Line = System.Windows.Shapes.Line;
using Point = System.Windows.Point;
using Shape = System.Windows.Shapes.Shape;

namespace SkyScope.Ui;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ConfigureNative();
    }

    private void ConfigureNative()
    {
        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(exStyle.ToInt64()
                | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_LAYERED));
        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    public void SetMonitorBounds(NativeMethods.RECT bounds)
    {
        if ((int)Left == bounds.Left && (int)Top == bounds.Top
            && (int)Width == bounds.Width && (int)Height == bounds.Height)
            return;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    /// Удерживает окно хрома строго над окном лупы.
    public void EnsureAbove(IntPtr magHwnd)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            MagnificationApi.SetWindowPos(hwnd, magHwnd, 0, 0, 0, 0,
                MagnificationApi.SWP_NOACTIVATE | MagnificationApi.SWP_NOMOVE | MagnificationApi.SWP_NOSIZE);
    }

    /// cx, cy — центр окна лупы (логические, относительно этого окна), w/h — его размер.
    public void UpdateChrome(double cx, double cy, double w, double h, Profile profile)
    {
        var scope = profile.Scope;
        var stroke = (Brush)new BrushConverter().ConvertFromString(scope.BorderColor)!;
        var border = (Shape)(scope.Shape == ScopeShape.Circle ? CircleBorder : SquareBorder);
        CircleBorder.Visibility = scope.Shape == ScopeShape.Circle ? Visibility.Visible : Visibility.Collapsed;
        SquareBorder.Visibility = scope.Shape == ScopeShape.Circle ? Visibility.Collapsed : Visibility.Visible;
        var isRounded = scope.Shape == ScopeShape.RoundedSquare || scope.Shape == ScopeShape.Rectangle;
        SquareBorder.RadiusX = isRounded ? 18 : 0;
        SquareBorder.RadiusY = isRounded ? 18 : 0;
        border.Width = w;
        border.Height = h;
        Canvas.SetLeft(border, cx - w / 2);
        Canvas.SetTop(border, cy - h / 2);
        border.Stroke = stroke;
        border.StrokeThickness = scope.BorderWidth;
        border.Opacity = scope.BorderOpacity;

        var showReticle = scope.Reticle;
        var reticleStroke = profile.Accessibility.HighContrastReticle ? Brushes.White : stroke;
        var reticleOpacity = scope.BorderOpacity;
        var gap = 8.0;
        var len = Math.Min(w, h) * 0.45;
        var thick = Math.Max(1.5, scope.BorderWidth * 0.6);
        UpdateLine(LineN, cx, cy - gap - len, cx, cy - gap, showReticle, reticleStroke, thick, reticleOpacity);
        UpdateLine(LineS, cx, cy + gap, cx, cy + gap + len, showReticle, reticleStroke, thick, reticleOpacity);
        UpdateLine(LineW, cx - gap - len, cy, cx - gap, cy, showReticle, reticleStroke, thick, reticleOpacity);
        UpdateLine(LineE, cx + gap, cy, cx + gap + len, cy, showReticle, reticleStroke, thick, reticleOpacity);

        CenterDot.Visibility = showReticle ? Visibility.Visible : Visibility.Collapsed;
        var dotR = Math.Max(2, scope.BorderWidth * 0.8);
        CenterDot.Width = dotR * 2;
        CenterDot.Height = dotR * 2;
        CenterDot.Fill = reticleStroke;
        CenterDot.Opacity = reticleOpacity;
        Canvas.SetLeft(CenterDot, cx - dotR);
        Canvas.SetTop(CenterDot, cy - dotR);

        Badge.Visibility = scope.ShowZoomBadge ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = $"{profile.Zoom.Level:0.0}×";
        Canvas.SetLeft(Badge, cx - Badge.ActualWidth / 2);
        Canvas.SetTop(Badge, cy + h / 2 - 34);

        if (scope.DimOutside)
        {
            DimRect.Visibility = Visibility.Visible;
            DimRect.Opacity = scope.DimOpacity;
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(new RectangleGeometry(new Rect(0, 0, Width, Height)));
            group.Children.Add(scope.Shape == ScopeShape.Circle
                ? new EllipseGeometry(new Point(cx, cy), w / 2, h / 2)
                : new RectangleGeometry(new Rect(cx - w / 2, cy - h / 2, w, h),
                    scope.Shape == ScopeShape.Square ? 0 : 18,
                    scope.Shape == ScopeShape.Square ? 0 : 18));
            DimRect.Clip = group;
        }
        else
        {
            DimRect.Visibility = Visibility.Collapsed;
        }
    }

    private static void UpdateLine(Line line, double x1, double y1, double x2, double y2,
        bool visible, Brush stroke, double thickness, double opacity)
    {
        line.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        line.X1 = x1; line.Y1 = y1; line.X2 = x2; line.Y2 = y2;
        line.Stroke = stroke;
        line.StrokeThickness = thickness;
        line.Opacity = opacity;
        line.StrokeStartLineCap = PenLineCap.Round;
        line.StrokeEndLineCap = PenLineCap.Round;
        line.SnapsToDevicePixels = true;
    }
}
