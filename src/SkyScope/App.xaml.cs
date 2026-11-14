using System.Drawing;
using System.IO;
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
    private const string ShowEventName = "SkyScope_ShowWindow_Event";

    public SettingsStore Store { get; } = new();
    public HotkeyManager Hotkeys { get; } = new();
    public OverlayService Overlay { get; private set; } = null!;
    public Ui.MainWindow? MainWindowInstance { get; private set; }
    public UpdateChecker Updates { get; private set; } = null!;

    private NotifyIcon? _tray;
    private EventWaitHandle? _showEvent;
    private ReleaseInfo? _pendingUpdate;

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
        ListenForShowEvent();

        Updates = new UpdateChecker(Store,
            e.Args.Contains("--update-test") ? new Version(0, 0, 1) : null);
        Updates.UpdateFound += OnUpdateFound;
        _ = Task.Run(UpdateLoopAsync);
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

        Ui.LoupeWindow.DumpFrameEnabled = e.Args.Contains("--frame-dump");
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
            MainWindowInstance.Topmost = true;
            MainWindowInstance.Topmost = false;
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
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_pendingUpdate is not null)
                Dispatcher.Invoke(() =>
                {
                    if (DownloadedSetupPath is not null)
                        InstallDownloadedUpdate();
                    else
                        StartUpdateFlow(_pendingUpdate);
                });
        };

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
        try
        {
            using var evt = EventWaitHandle.OpenExisting(ShowEventName);
            evt.Set();
        }
        catch (Exception ex)
        {
            Log($"signal FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Второй запуск программы будит первый через именованное событие —
    // окно показывается даже если было закрыто в трей.
    private void ListenForShowEvent()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                try { _showEvent.WaitOne(); }
                catch { return; }
                try { Dispatcher.Invoke(() => ShowMainWindow()); }
                catch (Exception ex) { Log($"show-event: {ex}"); }
            }
        }) { IsBackground = true };
        thread.Start();
    }

    private static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}";
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SkyScope");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "debug.log"), line);
        }
        catch { }
    }

    // ---------- Обновления ----------

    private void OnUpdateFound(ReleaseInfo release)
    {
        _pendingUpdate = release;
        Dispatcher.Invoke(() =>
        {
            if (MainWindowInstance is { IsVisible: true })
            {
                MainWindowInstance.ShowUpdateBanner(release);
            }
            else
            {
                EnsureTray();
                _tray?.ShowBalloonTip(8000, "SkyScope",
                    $"Доступна версия {release.Tag}. Нажми, чтобы скачать.", ToolTipIcon.Info);
            }
        });
        if (Store.Current.App.AutoDownloadUpdates)
            _ = DownloadUpdateAsync(release);
    }

    public string? DownloadedSetupPath { get; private set; }

    private async Task DownloadUpdateAsync(ReleaseInfo release)
    {
        var path = await Updates.DownloadSetupAsync(release);
        if (path is null || !ReferenceEquals(release, _pendingUpdate)) return;
        DownloadedSetupPath = path;
        await Dispatcher.InvokeAsync(() =>
        {
            MainWindowInstance?.ShowUpdateReady(release);
            if (MainWindowInstance is not { IsVisible: true })
            {
                EnsureTray();
                _tray?.ShowBalloonTip(8000, "SkyScope",
                    $"Версия {release.Tag} уже загружена. Нажми, чтобы установить.", ToolTipIcon.Info);
            }
        });
    }

    // Установка скачанного ранее установщика: тихий режим, AppMutex закроет приложение.
    public void InstallDownloadedUpdate()
    {
        if (DownloadedSetupPath is null) return;
        UpdateChecker.LaunchInstaller(DownloadedSetupPath, silent: true);
        Shutdown();
    }

    private async Task UpdateLoopAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        while (true)
        {
            if (Store.Current.App.CheckUpdates)
                await Updates.CheckAsync();
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    public async Task<bool> CheckUpdatesNow() => await Updates.CheckAsync() is not null;

    // Клик по balloon при выключенной автозагрузке: качаем и отдаём инсталлятору
    public void StartUpdateFlow(ReleaseInfo release)
    {
        _ = Task.Run(async () =>
        {
            var path = await Updates.DownloadSetupAsync(release);
            if (path is null) return;
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateChecker.LaunchInstaller(path, silent: true);
                Shutdown();
            });
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Hotkeys.UnregisterAll();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
