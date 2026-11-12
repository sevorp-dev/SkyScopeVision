using System.IO;
using System.Windows;
using System.Windows.Threading;
using SkyScope.Core;
using SkyScope.Native;

namespace SkyScope.Services;

/// <summary>
/// Управляет лупой: зона захвата — центр монитора курсора, окно вывода —
/// по якорю профиля. Тик нужен только для плавного зума, смены монитора
/// и живого перерисовывания кадров.
/// </summary>
public class OverlayService
{
    private readonly SettingsStore _store;
    private Ui.LoupeWindow? _mag;
    private Ui.DimWindow? _dim;
    private DispatcherTimer? _timer;
    private IntPtr _monitor;
    private uint _monitorDpi = 96;
    private NativeMethods.RECT _monitorBoundsPhysical;
    private double _displayedZoom = -1;
    private bool _forceUpdate = true;
    private bool _storeHooked;
    private long _lastRenderAt;

    public event Action<string>? Error;
    public event Action<bool>? Toggled;

    public bool IsActive { get; private set; }

    public bool MoveMode
    {
        get => _mag?.MoveMode == true;
        set { if (_mag is not null) _mag.MoveMode = value; }
    }

    public OverlayService(SettingsStore store)
    {
        _store = store;
    }

    public async Task<bool> Toggle()
    {
        if (IsActive)
        {
            await Hide();
            return false;
        }
        await Show();
        return true;
    }

    public Task Show()
    {
        if (IsActive) return Task.CompletedTask;
        IsActive = true;
        try
        {
            ShowCore();
        }
        catch (Exception ex)
        {
            IsActive = false;
            Log($"SHOW FAILED: {ex}");
            Error?.Invoke(ex.Message);
        }
        Toggled?.Invoke(IsActive);
        return Task.CompletedTask;
    }

    private void ShowCore()
    {
        if (!_storeHooked)
        {
            _storeHooked = true;
            _store.Changed += _ => _forceUpdate = true;
        }

        _mag ??= new Ui.LoupeWindow();
        _dim ??= new Ui.DimWindow();
        _mag.Moved -= OnMagMoved;
        _mag.Moved += OnMagMoved;
        _displayedZoom = -1;

        RefreshMonitor();
        _forceUpdate = true;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public Task Hide()
    {
        if (!IsActive) return Task.CompletedTask;
        IsActive = false;
        _timer?.Stop();
        _timer = null;
        _dim?.Hide();
        _mag?.Dispose();
        _mag = null;
        Toggled?.Invoke(false);
        return Task.CompletedTask;
    }

    private void OnMagMoved(int offsetX, int offsetY)
    {
        var scale = _monitorDpi / 96.0;
        _store.Update(s =>
        {
            s.ActiveProfile.Scope.Position = ScopePosition.Custom;
            s.ActiveProfile.Scope.CustomOffsetX = (int)(offsetX / scale);
            s.ActiveProfile.Scope.CustomOffsetY = (int)(offsetY / scale);
        });
        _forceUpdate = true;
    }

    private void RefreshMonitor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        _monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = NativeMethods.MONITORINFO.Create();
        if (NativeMethods.GetMonitorInfo(_monitor, ref info))
            _monitorBoundsPhysical = info.rcMonitor;
        if (NativeMethods.GetDpiForMonitor(_monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0)
            _monitorDpi = dpi;
    }

    private void Tick()
    {
        if (!IsActive || _mag is null) return;

        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor != _monitor)
        {
            _monitor = monitor;
            RefreshMonitor();
            _forceUpdate = true;
        }

        var profile = _store.Current.ActiveProfile;
        var zoom = profile.Zoom;
        var scale = _monitorDpi / 96.0;
        var fps = Math.Clamp(_store.Current.App.CaptureFps, 15, 120);
        var now = Environment.TickCount64;

        var animate = !profile.Accessibility.ReduceMotion && zoom.Smooth;
        if (animate)
        {
            if (_displayedZoom < 0) _displayedZoom = zoom.Level;
            else _displayedZoom += (zoom.Level - _displayedZoom) * 0.3;
            if (Math.Abs(_displayedZoom - zoom.Level) < 0.005) _displayedZoom = zoom.Level;
        }
        else
        {
            _displayedZoom = zoom.Level;
        }

        var zoomAnimating = Math.Abs(_displayedZoom - zoom.Level) > 0.0001;
        if (!zoomAnimating && !_forceUpdate && now - _lastRenderAt < 1000 / fps) return;
        _forceUpdate = false;
        _lastRenderAt = now;

        var cx = _monitorBoundsPhysical.Left + _monitorBoundsPhysical.Width / 2;
        var cy = _monitorBoundsPhysical.Top + _monitorBoundsPhysical.Height / 2;

        _mag.Update(cx, cy, scale, profile);

        if (profile.Scope.DimOutside)
        {
            _dim?.Update(_monitorBoundsPhysical, profile.Scope,
                _mag.WindowX, _mag.WindowY, _mag.WindowW, _mag.WindowH);
        }
        else
        {
            _dim?.Hide();
        }

        _mag.Render(scale, _displayedZoom, profile);
    }

    internal static void Log(string message)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SkyScope", "debug.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [overlay] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
