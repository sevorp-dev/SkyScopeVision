using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using SkyScope.Core;
using SkyScope.Services;
using Application = System.Windows.Application;

namespace SkyScope;

public partial class App : Application
{
    private const string MutexName = "SkyScope_SingleInstance_Mutex";

    public SettingsStore Store { get; } = new();
    public HotkeyManager Hotkeys { get; } = new();
    public OverlayService Overlay { get; private set; } = null!;
    public Ui.MainWindow? MainWindowInstance { get; private set; }

    private NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        var mutex = Native.NativeMethods.CreateMutex(IntPtr.Zero, true, MutexName);
        if (Native.NativeMethods.GetLastError() == 183)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Overlay = new OverlayService(Store);
        Overlay.Error += msg => Dispatcher.Invoke(() =>
            MainWindowInstance?.ShowToast($"Ошибка захвата: {msg}"));

        Hotkeys.Triggered += OnHotkey;
        Hotkeys.RegistrationFailed += (action, combo) =>
            MainWindowInstance?.ShowToast($"Комбинацию «{combo}» заняла система", true);

        var startMinimized = e.Args.Contains("--minimized");
        ShowMainWindow(startMinimized);
        Hotkeys.RegisterAll(Store.Current.Hotkeys);

        if (e.Args.Contains("--overlay-test"))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                await Dispatcher.InvokeAsync(async () => await Overlay.Show());
            });
        }
    }

    public void ShowMainWindow(bool minimized = false)
    {
        if (MainWindowInstance is null || !MainWindowInstance.IsLoaded)
        {
            MainWindowInstance = new Ui.MainWindow(Store, Overlay, Hotkeys);
            MainWindowInstance.Closed += (_, _) => MainWindowInstance = null;
            var helper = new WindowInteropHelper(MainWindowInstance);
            helper.EnsureHandle();
            Hotkeys.Hwnd = helper.Handle;
            Hotkeys.Attach(helper);
        }
        if (minimized)
        {
            MainWindowInstance.WindowState = WindowState.Minimized;
            MainWindowInstance.Hide();
        }
        else
        {
            MainWindowInstance.Show();
            MainWindowInstance.WindowState = WindowState.Normal;
            MainWindowInstance.Activate();
        }
        EnsureTray();
    }

    public void EnsureTray()
    {
        if (_tray is not null || !Store.Current.App.ShowTrayIcon) return;
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SkyScope.Assets.icon.ico")!;
            _tray = new NotifyIcon
            {
                Icon = new System.Drawing.Icon(stream),
                Text = "SkyScope",
                Visible = true,
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tray unavailable: {ex.Message}");
            return;
        }
        _tray.DoubleClick += (_, _) => ShowMainWindow();

        var menu = new ContextMenuStrip();
        var toggleItem = new ToolStripMenuItem("Включить прицел");
        toggleItem.Click += async (_, _) => await Overlay.Toggle();
        var settingsItem = new ToolStripMenuItem("Настройки");
        settingsItem.Click += (_, _) => ShowMainWindow();
        var quitItem = new ToolStripMenuItem("Выход");
        quitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(toggleItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);
        _tray.ContextMenuStrip = menu;

        Overlay.Toggled += active => toggleItem.Text = active ? "Выключить прицел" : "Включить прицел";
    }

    private void OnHotkey(HotkeyAction action)
    {
        var profile = Store.Current.ActiveProfile;
        switch (action)
        {
            case HotkeyAction.Toggle:
                _ = Overlay.Toggle().ContinueWith(t =>
                    MainWindowInstance?.SetOverlaySwitch(t.Result));
                break;
            case HotkeyAction.ZoomIn:
                ApplyZoom(profile.Zoom.Level + profile.Zoom.Step);
                break;
            case HotkeyAction.ZoomOut:
                ApplyZoom(profile.Zoom.Level - profile.Zoom.Step);
                break;
            case HotkeyAction.Reset:
                ApplyZoom(2);
                break;
        }
    }

    private void ApplyZoom(double level)
    {
        Store.Update(s =>
        {
            var zoom = s.ActiveProfile.Zoom;
            zoom.Level = Math.Round(Math.Clamp(level, zoom.Min, zoom.Max) * 100) / 100;
        });
        MainWindowInstance?.RefreshBindings();
    }

    private void ActivateExistingInstance()
    {
        Native.NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!Native.NativeMethods.IsWindowVisible(hwnd)) return true;
            Native.NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            // Подсветить существующее окно нельзя без PID — оставляем просто выход.
            return true;
        }, IntPtr.Zero);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Hotkeys.UnregisterAll();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
