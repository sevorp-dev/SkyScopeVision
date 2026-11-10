using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shell;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.Windows.Shapes.Path;
using TextBox = System.Windows.Controls.TextBox;
using SkyScope.Core;
using SkyScope.Services;

namespace SkyScope.Ui;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private readonly OverlayService _overlay;
    private readonly HotkeyManager _hotkeys;

    private static readonly (HotkeyAction Action, string Name, string Icon)[] HotkeyRows =
    [
        (HotkeyAction.Toggle, "Глобальное переключение", "M12 3v8 M6.3 6.5a8 8 0 1 0 11.4 0"),
        (HotkeyAction.ZoomIn, "Увеличить зум", "M12 5v14M5 12h14"),
        (HotkeyAction.ZoomOut, "Уменьшить зум", "M5 12h14"),
        (HotkeyAction.Reset, "Сбросить зум", "M4 5v5h5 M4.6 14a8 8 0 1 0 1.2-6.4L4 10"),
    ];

    private static readonly string[] SwatchColors =
        ["#FF7D1F", "#00E676", "#40C4FF", "#FF5252", "#E040FB", "#FFD740", "#FFFFFF"];

    private bool _suppressEvents;
    private HotkeyAction? _listeningHotkey;
    private Button? _listeningButton;

    public MainWindow(SettingsStore store, OverlayService overlay, HotkeyManager hotkeys)
    {
        _store = store;
        _overlay = overlay;
        _hotkeys = hotkeys;
        InitializeComponent();
        BuildColorSwatches();
        RefreshBindings();
        _overlay.Toggled += active => Dispatcher.Invoke(() => OverlaySwitch.IsChecked = active);
        _store.Changed += _ => Dispatcher.Invoke(RefreshBindings);
    }

    // ---------- Bindings ----------

    public void RefreshBindings()
    {
        _suppressEvents = true;
        try
        {
            var profile = _store.Current.ActiveProfile;

            HomeZoomSlider.Value = profile.Zoom.Level;
            HomeZoomSlider.Minimum = profile.Zoom.Min;
            HomeZoomSlider.Maximum = profile.Zoom.Max;
            HomeZoomValue.Text = $"{profile.Zoom.Level:0.00}×";
            ResetProfileDesc.Text = $"Вернуть зум и оформление профиля «{profile.Name}» к заводским";
            ScopeSub.Text = $"Параметры активного профиля «{profile.Name}»";

            F_ZoomLevel.SetValueSilently(profile.Zoom.Level);
            F_ZoomLevel.Minimum = profile.Zoom.Min;
            F_ZoomLevel.Maximum = profile.Zoom.Max;
            F_ZoomStep.SetValueSilently(profile.Zoom.Step);
            F_ZoomMin.SetValueSilently(profile.Zoom.Min);
            F_ZoomMax.SetValueSilently(profile.Zoom.Max);
            T_ZoomSmooth.IsChecked = profile.Zoom.Smooth;
            T_ZoomBadge.IsChecked = profile.Scope.ShowZoomBadge;

            F_Radius.SetValueSilently(profile.Scope.Radius);
            F_BorderWidth.SetValueSilently(profile.Scope.BorderWidth);
            F_BorderOpacity.SetValueSilently(profile.Scope.BorderOpacity);
            F_DimOpacity.SetValueSilently(profile.Scope.DimOpacity);
            T_Reticle.IsChecked = profile.Scope.Reticle;
            T_Dim.IsChecked = profile.Scope.DimOutside;
            UpdateShapeButtons(profile.Scope.Shape);
            UpdatePositionButtons(profile.Scope.Position);
            UpdateColorSwatches(profile.Scope.BorderColor);
            T_MoveMode.IsChecked = _overlay.MoveMode;

            T_Contrast.IsChecked = profile.Accessibility.HighContrastReticle;
            T_ReduceMotion.IsChecked = profile.Accessibility.ReduceMotion;

            T_Autostart.IsChecked = Autostart.IsEnabled();
            T_Tray.IsChecked = _store.Current.App.ShowTrayIcon;
            UpdateFpsButtons(_store.Current.App.CaptureFps);

            UpdateHotkeyLabels();
            BuildProfileChips();
            BuildHelpHotkeys();
            OverlaySwitch.IsChecked = _overlay.IsActive;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void SetOverlaySwitch(bool active) => OverlaySwitch.IsChecked = active;

    // ---------- Navigation ----------

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var nav = (Button)sender;
        foreach (var btn in new[] { NavHome, NavScope, NavApp, NavHelp })
            btn.Tag = ReferenceEquals(btn, nav) ? "active" : null;

        PageHome.Visibility = ReferenceEquals(nav, NavHome) ? Visibility.Visible : Visibility.Collapsed;
        PageScope.Visibility = ReferenceEquals(nav, NavScope) ? Visibility.Visible : Visibility.Collapsed;
        PageApp.Visibility = ReferenceEquals(nav, NavApp) ? Visibility.Visible : Visibility.Collapsed;
        PageHelp.Visibility = ReferenceEquals(nav, NavHelp) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        var tab = (Button)sender;
        foreach (var t in new[] { TabZoom, TabLook, TabAccess })
            t.Tag = ReferenceEquals(t, tab) ? "active" : null;
        PanelZoom.Visibility = ReferenceEquals(tab, TabZoom) ? Visibility.Visible : Visibility.Collapsed;
        PanelLook.Visibility = ReferenceEquals(tab, TabLook) ? Visibility.Visible : Visibility.Collapsed;
        PanelAccess.Visibility = ReferenceEquals(tab, TabAccess) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Titlebar ----------

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(8),
            CornerRadius = new CornerRadius(0),
        });
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_store.Current.App.ShowTrayIcon)
        {
            Hide();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    private async void OverlaySwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (OverlaySwitch.IsChecked == _overlay.IsActive) return;
        await _overlay.Toggle();
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/sevorp-dev/SkyScopeVision") { UseShellExecute = true });

    private void Link_Click(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ---------- Profiles ----------

    private void BuildProfileChips()
    {
        ProfilesPanel.Children.Clear();
        foreach (var profile in _store.Current.Profiles)
        {
            var chip = new Button
            {
                Style = (Style)FindResource("ProfileChip"),
                Content = profile.Name,
                Tag = profile.Id,
            };
            if (profile.Id == _store.Current.ActiveProfileId)
            {
                chip.Foreground = (Brush)FindResource("AccentBrush");
                chip.Background = (Brush)FindResource("AccentSoftBrush");
                chip.BorderBrush = (Brush)FindResource("AccentBrush");
            }
            chip.Click += (_, _) =>
            {
                _store.Update(s => s.ActiveProfileId = profile.Id);
            };
            chip.MouseDoubleClick += (_, _) =>
            {
                var index = ProfilesPanel.Children.IndexOf(chip);
                var box = new TextBox
                {
                    Text = profile.Name,
                    Width = 110,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = (Brush)FindResource("AccentBrush"),
                    FontWeight = FontWeights.SemiBold,
                };
                ProfilesPanel.Children[index] = box;
                box.Focus();
                box.SelectAll();
                void Commit(object? s2, RoutedEventArgs e2)
                {
                    _store.Update(st => st.Profiles.First(p => p.Id == profile.Id).Name =
                        string.IsNullOrWhiteSpace(box.Text) ? profile.Name : box.Text.Trim());
                    box.LostFocus -= Commit;
                    box.KeyDown -= KeyDown;
                }
                void KeyDown(object? s2, KeyEventArgs e2)
                {
                    if (e2.Key == Key.Enter) box.LostFocus -= Commit;
                    if (e2.Key == Key.Enter || e2.Key == Key.Escape) Commit(s2, e2);
                }
                box.LostFocus += Commit;
                box.KeyDown += KeyDown;
            };

            if (_store.Current.Profiles.Count > 1)
            {
                var del = new Button
                {
                    Content = "×",
                    Width = 18, Height = 18,
                    Margin = new Thickness(8, 0, -6, 0),
                    Background = Brushes.Transparent,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    BorderThickness = new Thickness(0),
                    FontSize = 13,
                    ToolTip = "Удалить профиль",
                };
                del.Click += (_, e2) =>
                {
                    e2.Handled = true;
                    _store.Update(s => s.DeleteProfile(profile.Id));
                };
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new TextBlock { Text = profile.Name, VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(del);
                chip.Content = panel;
            }
            ProfilesPanel.Children.Add(chip);
        }

        var add = new Button
        {
            Style = (Style)FindResource("ActionButton"),
            Content = "+ Новый",
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x52)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xB8, 0xDD)),
        };
        add.Click += (_, _) => _store.Update(s => s.CreateProfile(null));
        ProfilesPanel.Children.Add(add);
    }

    // ---------- Hotkeys ----------

    private void UpdateHotkeyLabels()
    {
        foreach (var child in HotkeysGrid.Children.OfType<Grid>().ToList())
            HotkeysGrid.Children.Remove(child);

        for (var i = 0; i < HotkeyRows.Length; i++)
        {
            var (action, name, icon) = HotkeyRows[i];
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(new Path
            {
                Data = Geometry.Parse(icon),
                Stretch = Stretch.Uniform,
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 12, 0),
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                VerticalAlignment = VerticalAlignment.Center,
            });
            left.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

            var chip = new Button
            {
                Style = (Style)FindResource("KeyChip"),
                Tag = action,
                Content = GetHotkeyCombo(action),
            };
            chip.Click += HotkeyChip_Click;

            Grid.SetColumn(left, 0);
            Grid.SetColumn(chip, 1);
            row.Children.Add(left);
            row.Children.Add(chip);
            Grid.SetRow(row, i);
            HotkeysGrid.Children.Add(row);
        }
    }

    private string GetHotkeyCombo(HotkeyAction action) => action switch
    {
        HotkeyAction.Toggle => _store.Current.Hotkeys.Toggle,
        HotkeyAction.ZoomIn => _store.Current.Hotkeys.ZoomIn,
        HotkeyAction.ZoomOut => _store.Current.Hotkeys.ZoomOut,
        HotkeyAction.Reset => _store.Current.Hotkeys.Reset,
        _ => "",
    };

    private void HotkeyChip_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (_listeningHotkey.HasValue && _listeningButton is not null)
            _listeningButton.Content = GetHotkeyCombo(_listeningHotkey.Value);
        _listeningHotkey = (HotkeyAction)button.Tag;
        _listeningButton = button;
        button.Content = "Нажмите клавиши…";
        button.Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_listeningHotkey.HasValue) return;

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            StopListening();
            return;
        }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        var mods = ModifierKeys.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= ModifierKeys.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= ModifierKeys.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= ModifierKeys.Shift;

        var vk = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        var nativeMods = 0u;
        if (mods.HasFlag(ModifierKeys.Control)) nativeMods |= Native.NativeMethods.MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Alt)) nativeMods |= Native.NativeMethods.MOD_ALT;
        if (mods.HasFlag(ModifierKeys.Shift)) nativeMods |= Native.NativeMethods.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) nativeMods |= Native.NativeMethods.MOD_WIN;

        var isFunctionKey = vk is >= 0x70 and <= 0x7B;
        var hasModifier = nativeMods != 0;
        if (!hasModifier && !isFunctionKey)
        {
            ShowToast("Нужен хотя бы один модификатор (Ctrl / Alt / Shift)", true);
            return;
        }

        var combo = HotkeyParser.Format(nativeMods, (uint)vk);
        var all = new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.Toggle] = _store.Current.Hotkeys.Toggle,
            [HotkeyAction.ZoomIn] = _store.Current.Hotkeys.ZoomIn,
            [HotkeyAction.ZoomOut] = _store.Current.Hotkeys.ZoomOut,
            [HotkeyAction.Reset] = _store.Current.Hotkeys.Reset,
        };
        var conflict = all.FirstOrDefault(kv => kv.Key != _listeningHotkey.Value && kv.Value == combo);
        if (conflict.Value is not null)
        {
            ShowToast($"Комбинация занята: {NameOf(conflict.Key)}", true);
            StopListening();
            return;
        }

        var action = _listeningHotkey.Value;
        _store.Update(s =>
        {
            switch (action)
            {
                case HotkeyAction.Toggle: s.Hotkeys.Toggle = combo; break;
                case HotkeyAction.ZoomIn: s.Hotkeys.ZoomIn = combo; break;
                case HotkeyAction.ZoomOut: s.Hotkeys.ZoomOut = combo; break;
                case HotkeyAction.Reset: s.Hotkeys.Reset = combo; break;
            }
        });
        _hotkeys.RegisterAll(_store.Current.Hotkeys);
        ShowToast($"{NameOf(action)}: {combo}");
        StopListening();
    }

    private void StopListening()
    {
        if (_listeningButton is not null)
            _listeningButton.Content = GetHotkeyCombo(_listeningHotkey.GetValueOrDefault());
        _listeningHotkey = null;
        _listeningButton = null;
    }

    private static string NameOf(HotkeyAction action) => HotkeyRows.First(r => r.Action == action).Name;

    private void BuildHelpHotkeys()
    {
        HelpHotkeys.Children.Clear();
        foreach (var (action, name, _) in HotkeyRows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var kbd = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 3, 10, 3),
                MinWidth = 120,
                Child = new TextBlock { Text = GetHotkeyCombo(action), FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center },
            };
            row.Children.Add(kbd);
            row.Children.Add(new TextBlock
            {
                Text = "  " + name,
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            HelpHotkeys.Children.Add(row);
        }
    }

    // ---------- Profile settings ----------

    private void HomeZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        _store.Update(s => s.ActiveProfile.Zoom.Level = Math.Round(e.NewValue * 4) / 4);
    }

    private void ProfileZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        _store.Update(s =>
        {
            var zoom = s.ActiveProfile.Zoom;
            zoom.Level = F_ZoomLevel.Value;
            zoom.Step = F_ZoomStep.Value;
            zoom.Min = Math.Min(F_ZoomMin.Value, F_ZoomMax.Value - 0.25);
            zoom.Max = Math.Max(F_ZoomMax.Value, F_ZoomMin.Value + 0.25);
            zoom.Smooth = T_ZoomSmooth.IsChecked == true;
            s.ActiveProfile.Scope.ShowZoomBadge = T_ZoomBadge.IsChecked == true;
        });
    }

    private void ProfileZoomToggle_Changed(object sender, RoutedEventArgs e) => ProfileZoom_Changed(sender, new RoutedPropertyChangedEventArgs<double>(0, 0));

    private void ProfileScope_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        _store.Update(s =>
        {
            var scope = s.ActiveProfile.Scope;
            scope.Radius = (int)F_Radius.Value;
            scope.BorderWidth = F_BorderWidth.Value;
            scope.BorderOpacity = F_BorderOpacity.Value;
            scope.DimOpacity = F_DimOpacity.Value;
            scope.Reticle = T_Reticle.IsChecked == true;
            scope.DimOutside = T_Dim.IsChecked == true;
        });
    }

    private void ProfileScopeToggle_Changed(object sender, RoutedEventArgs e) => ProfileScope_Changed(sender, new RoutedPropertyChangedEventArgs<double>(0, 0));

    private void ProfileAccess_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _store.Update(s =>
        {
            s.ActiveProfile.Accessibility.HighContrastReticle = T_Contrast.IsChecked == true;
            s.ActiveProfile.Accessibility.ReduceMotion = T_ReduceMotion.IsChecked == true;
        });
    }

    private void ShapeCircle_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(s => s.ActiveProfile.Scope.Shape = ScopeShape.Circle);
        UpdateShapeButtons(ScopeShape.Circle);
    }

    private void ShapeSquare_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(s => s.ActiveProfile.Scope.Shape = ScopeShape.Square);
        UpdateShapeButtons(ScopeShape.Square);
    }

    private void ShapeRectangle_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(s => s.ActiveProfile.Scope.Shape = ScopeShape.Rectangle);
        UpdateShapeButtons(ScopeShape.Rectangle);
    }

    private void ShapeRounded_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(s => s.ActiveProfile.Scope.Shape = ScopeShape.RoundedSquare);
        UpdateShapeButtons(ScopeShape.RoundedSquare);
    }

    private void UpdateShapeButtons(ScopeShape shape)
    {
        var names = new[] { ScopeShape.Circle, ScopeShape.Square, ScopeShape.Rectangle, ScopeShape.RoundedSquare };
        for (var i = 0; i < names.Length; i++)
            StyleButton((Button)ShapeSeg.Children[i], names[i] == shape);
    }

    private void PosCenter_Click(object sender, RoutedEventArgs e) => SetPosition(ScopePosition.Center);
    private void PosTop_Click(object sender, RoutedEventArgs e) => SetPosition(ScopePosition.Top);
    private void PosBottom_Click(object sender, RoutedEventArgs e) => SetPosition(ScopePosition.Bottom);
    private void PosLeft_Click(object sender, RoutedEventArgs e) => SetPosition(ScopePosition.Left);
    private void PosRight_Click(object sender, RoutedEventArgs e) => SetPosition(ScopePosition.Right);
    private void PosCustom_Click(object sender, RoutedEventArgs e) => SetPosition(ScopePosition.Custom);

    private void SetPosition(ScopePosition pos)
    {
        _store.Update(s => s.ActiveProfile.Scope.Position = pos);
        UpdatePositionButtons(pos);
        if (pos == ScopePosition.Custom && !_overlay.IsActive)
            ShowToast("Включи прицел, затем активируй «Ручное размещение»");
    }

    private void UpdatePositionButtons(ScopePosition pos)
    {
        var names = new[]
        {
            ScopePosition.Center, ScopePosition.Top, ScopePosition.Bottom,
            ScopePosition.Left, ScopePosition.Right, ScopePosition.Custom,
        };
        for (var i = 0; i < names.Length; i++)
            StyleButton((Button)PosSeg.Children[i], names[i] == pos);
    }

    private void MoveMode_Changed(object sender, RoutedEventArgs e)
    {
        var on = T_MoveMode.IsChecked == true;
        _overlay.MoveMode = on && _overlay.IsActive;
        ShowToast(on
            ? "Перетаскивай окно прицела мышкой. Повторное нажатие — закрепить"
            : "Позиция закреплена");
    }

    private void UpdateFpsButtons(int fps)
    {
        StyleButton((Button)FpsSeg.Children[0], fps == 30);
        StyleButton((Button)FpsSeg.Children[1], fps == 60);
    }

    private static void StyleButton(Button button, bool active)
    {
        if (active)
        {
            button.Background = (Brush)Application.Current.FindResource("AccentSoftBrush");
            button.BorderBrush = (Brush)Application.Current.FindResource("AccentBrush");
            button.Foreground = (Brush)Application.Current.FindResource("AccentBrush");
        }
        else
        {
            button.Background = (Brush)Application.Current.FindResource("Panel2Brush");
            button.BorderBrush = (Brush)Application.Current.FindResource("BorderBrush");
            button.Foreground = (Brush)Application.Current.FindResource("MutedBrush");
        }
    }

    private void Fps30_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(s => s.App.CaptureFps = 30);
        UpdateFpsButtons(30);
    }

    private void Fps60_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(s => s.App.CaptureFps = 60);
        UpdateFpsButtons(60);
    }

    // ---------- Colors ----------

    private void BuildColorSwatches()
    {
        foreach (var color in SwatchColors)
        {
            var swatch = new Button
            {
                Width = 26, Height = 26,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (Brush)new BrushConverter().ConvertFromString(color)!,
                BorderThickness = new Thickness(2),
                Tag = color,
                ToolTip = color,
            };
            swatch.Click += (_, _) =>
            {
                _store.Update(s => s.ActiveProfile.Scope.BorderColor = color);
                UpdateColorSwatches(color);
            };
            ColorSwatches.Children.Add(swatch);
        }
    }

    private void UpdateColorSwatches(string active)
    {
        foreach (var child in ColorSwatches.Children.OfType<Button>())
        {
            child.BorderBrush = string.Equals(child.Tag as string, active, StringComparison.OrdinalIgnoreCase)
                ? Brushes.White
                : Brushes.Transparent;
        }
    }

    // ---------- App settings ----------

    private void AppToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ReferenceEquals(sender, T_Autostart))
        {
            Autostart.SetEnabled(T_Autostart.IsChecked == true);
        }
        else
        {
            _store.Update(s => s.App.ShowTrayIcon = T_Tray.IsChecked == true);
        }
    }

    private void ResetProfile_Click(object sender, RoutedEventArgs e)
    {
        var id = _store.Current.ActiveProfileId;
        var name = _store.Current.ActiveProfile.Name;
        _store.Update(s =>
        {
            var index = s.Profiles.ToList().FindIndex(p => p.Id == id);
            var fresh = new Profile { Id = id, Name = name };
            s.Profiles[index] = fresh;
        });
        ShowToast("Профиль сброшен");
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Сбросить все настройки SkyScope?", "SkyScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;
        _store.Update(s =>
        {
            var fresh = Settings.Defaults();
            s.Hotkeys = fresh.Hotkeys;
            s.Profiles = fresh.Profiles;
            s.ActiveProfileId = fresh.ActiveProfileId;
            s.App = fresh.App;
        });
        _hotkeys.RegisterAll(_store.Current.Hotkeys);
        Autostart.SetEnabled(false);
        ShowToast("Настройки сброшены");
    }

    // ---------- Toast ----------

    private DispatcherTimer? _toastTimer;

    public void ShowToast(string message, bool isError = false)
    {
        ToastText.Text = message;
        Toast.BorderBrush = isError ? (Brush)FindResource("DangerBrush") : (Brush)FindResource("BorderBrush");
        ToastText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0xAE))
            : (Brush)FindResource("TextBrush");
        Toast.Visibility = Visibility.Visible;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _toastTimer.Tick += (_, _) =>
        {
            Toast.Visibility = Visibility.Collapsed;
            _toastTimer.Stop();
        };
        _toastTimer.Start();
    }
}
