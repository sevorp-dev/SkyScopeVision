using System.Windows;
using System.Windows.Threading;
using SkyScope.Core;
using SkyScope.Native;

namespace SkyScope.Services;

/// <summary>
/// Управляет прицелом: зона захвата — центр монитора курсора, окно вывода —
/// по якорю профиля. Тик-таймер нужен только для плавного зума и смены монитора.
/// </summary>
public class OverlayService
{
    private readonly SettingsStore _store;
    private Ui.OverlayWindow? _window;
    private Ui.MagWindow? _mag;
    private DispatcherTimer? _timer;
    private IntPtr _monitor;
    private uint _monitorDpi = 96;
    private NativeMethods.RECT _monitorBoundsPhysical;
    private double _displayedZoom = -1;
    private bool _forceUpdate = true;
    private bool _storeHooked;

    public event Action<string>? Error;
    public event Action<bool>? Toggled;

    public bool IsActive { get; private set; }

    public bool MoveMode
    {
        get => _mag?.MoveMode == true;
        set { if (_mag is not null) _mag.MoveMode = value; }
    }

    public event Action? MoveModeChanged;

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

        if (!_storeHooked)
        {
            _storeHooked = true;
            _store.Changed += _ => _forceUpdate = true;
        }

        _mag ??= new Ui.MagWindow();
        _window ??= new Ui.OverlayWindow();
        _mag.Moved -= OnMagMoved;
        _mag.Moved += OnMagMoved;
        _displayedZoom = -1;

        RefreshMonitor();
        _window.Show();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Toggled?.Invoke(true);
        return Task.CompletedTask;
    }

    public Task Hide()
    {
        if (!IsActive) return Task.CompletedTask;
        IsActive = false;
        _timer?.Stop();
        _timer = null;
        _window?.Hide();
        _mag?.Dispose();
        _mag = null;
        Toggled?.Invoke(false);
        MoveModeChanged?.Invoke();
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
        var scale = _monitorDpi / 96.0;

        if (_window is not null)
        {
            _window.SetMonitorBounds(new NativeMethods.RECT
            {
                Left = (int)(_monitorBoundsPhysical.Left / scale),
                Top = (int)(_monitorBoundsPhysical.Top / scale),
                Right = (int)(_monitorBoundsPhysical.Right / scale),
                Bottom = (int)(_monitorBoundsPhysical.Bottom / scale),
            });
        }
    }

    private void Tick()
    {
        if (!IsActive || _window is null || _mag is null) return;

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
        if (!zoomAnimating && !_forceUpdate) return;
        _forceUpdate = false;

        var cx = _monitorBoundsPhysical.Left + _monitorBoundsPhysical.Width / 2;
        var cy = _monitorBoundsPhysical.Top + _monitorBoundsPhysical.Height / 2;

        var animProfile = CloneWithZoom(profile, _displayedZoom);
        _mag.Update(cx, cy, scale, animProfile);
        _window.EnsureAbove(_mag.Handle);

        var (w, h) = Ui.MagWindow.WindowSize(profile.Scope, scale);
        var chromeX = (cx - _monitorBoundsPhysical.Left) / scale;
        var chromeY = (cy - _monitorBoundsPhysical.Top) / scale;
        _window.UpdateChrome(chromeX, chromeY, w / scale, h / scale, profile);
    }

    private static Profile CloneWithZoom(Profile source, double level)
    {
        return new Profile
        {
            Id = source.Id,
            Name = source.Name,
            Scope = source.Scope,
            Zoom = new ZoomSettings
            {
                Level = level,
                Step = source.Zoom.Step,
                Min = source.Zoom.Min,
                Max = source.Zoom.Max,
                Smooth = source.Zoom.Smooth,
            },
            Accessibility = source.Accessibility,
        };
    }
}
