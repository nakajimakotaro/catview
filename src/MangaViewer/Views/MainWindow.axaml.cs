using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MangaViewer.Core;
using MangaViewer.Core.Imaging;
using MangaViewer.Core.Persistence;
using MangaViewer.ViewModels;

namespace MangaViewer.Views;

public partial class MainWindow : Window
{
    private static readonly int[] PrefetchChoices = [0, 2, 4, 6, 10, 16];
    private static readonly int[] CacheChoices = [256, 512, 1024, 2048, 4096];

    private WindowState _stateBeforeFullScreen = WindowState.Normal;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    public MainWindow()
    {
        InitializeComponent();

        // メニューやシークバーより先にショートカットを処理するため、トンネルで受け取る
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        PageView.NavigateRequested += delta => ViewModel?.StepUnit(delta);
        PageView.ToggleFullScreenRequested += ToggleFullScreen;
        PageView.ViewChanged += () => ViewModel?.OnViewChanged();

        BuildChoiceMenu(PrefetchMenu, PrefetchChoices, v => v == 0 ? "なし" : $"{v} ページ",
            () => ViewModel?.PrefetchCount, v => ViewModel?.SetPrefetchCount(v));
        BuildChoiceMenu(CacheMenu, CacheChoices, v => $"{v} MB",
            () => ViewModel?.CacheLimitMB, v => ViewModel?.SetCacheLimit(v));

        PositionChanged += (_, _) => RememberNormalBounds();
        SizeChanged += (_, _) => RememberNormalBounds();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.TargetSizeProvider = PageView.ComputeTargetSizes;
            RestorePlacement(vm.WindowPlacement);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PageView.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (ViewModel is { } vm)
        {
            vm.WindowPlacement = new WindowPlacement
            {
                X = _normalPosition.X,
                Y = _normalPosition.Y,
                Width = _normalSize.Width,
                Height = _normalSize.Height,
                Maximized = WindowState == WindowState.Maximized
                    || (WindowState == WindowState.FullScreen && _stateBeforeFullScreen == WindowState.Maximized),
            };
        }
    }

    // ---- ウィンドウ位置・サイズ ----

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight) return;
        var position = new PixelPoint(placement.X, placement.Y);
        // 保存時の画面が取り外されている場合は位置を復元しない
        var visible = Screens.All.Any(s => s.WorkingArea.Contains(position + new PixelPoint(40, 40)));
        Width = placement.Width;
        Height = placement.Height;
        if (visible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
        }
        _normalPosition = position;
        _normalSize = new Size(placement.Width, placement.Height);
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void RememberNormalBounds()
    {
        if (WindowState != WindowState.Normal) return;
        _normalPosition = Position;
        _normalSize = ClientSize;
    }

    // ---- フルスクリーン ----

    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            _stateBeforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
            if (ViewModel is { } vm) vm.IsFullScreen = true;
        }
    }

    private void ExitFullScreen()
    {
        if (WindowState != WindowState.FullScreen) return;
        WindowState = _stateBeforeFullScreen;
        if (ViewModel is { } vm) vm.IsFullScreen = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // macOS の緑ボタンなど、OS 側からフルスクリーンが変わった場合も追従する
        if (change.Property == WindowStateProperty && ViewModel is { } vm)
        {
            vm.IsFullScreen = WindowState == WindowState.FullScreen;
        }
    }

    // ---- キーボード（仕様 6.1） ----

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if (MainMenu.IsOpen) return;

        var mods = e.KeyModifiers;
        // macOS では Command キーも Ctrl と同様に扱う
        var ctrl = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        var shift = mods.HasFlag(KeyModifiers.Shift);
        var alt = mods.HasFlag(KeyModifiers.Alt);
        var rtl = vm.Direction == ReadingDirection.RightToLeft;
        var handled = true;

        switch (e.Key)
        {
            case Key.Left when ctrl:
                _ = vm.PreviousFile();
                break;
            case Key.Right when ctrl:
                _ = vm.NextFile();
                break;
            case Key.Left when shift:
                vm.StepPage(rtl ? 1 : -1);
                break;
            case Key.Right when shift:
                vm.StepPage(rtl ? -1 : 1);
                break;
            case Key.Left when !alt:
                vm.StepUnit(rtl ? 1 : -1);
                break;
            case Key.Right when !alt:
                vm.StepUnit(rtl ? -1 : 1);
                break;
            case Key.Space:
                vm.StepUnit(shift ? -1 : 1);
                break;
            case Key.PageDown:
                vm.StepUnit(1);
                break;
            case Key.PageUp:
                vm.StepUnit(-1);
                break;
            case Key.Home when !ctrl:
                vm.FirstPage();
                break;
            case Key.End when !ctrl:
                vm.LastPage();
                break;
            case Key.O when ctrl:
                _ = OpenFileAsync();
                break;
            case Key.W when ctrl:
                vm.CloseFile();
                break;
            case Key.OemPlus or Key.Add when ctrl:
                PageView.ZoomBy(1.25);
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                PageView.ZoomBy(1 / 1.25);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                vm.FitMode = FitMode.Window;
                PageView.ResetView();
                break;
            case Key.F when !ctrl && !alt:
            case Key.F11:
            case Key.Enter when !ctrl && !alt:
                ToggleFullScreen();
                break;
            case Key.Escape when WindowState == WindowState.FullScreen:
                ExitFullScreen();
                break;
            case Key.D when !ctrl && !alt:
                vm.ToggleShift();
                break;
            case Key.D1 or Key.NumPad1 when !ctrl && !alt:
                vm.ViewMode = ViewMode.Single;
                break;
            case Key.D2 or Key.NumPad2 when !ctrl && !alt:
                vm.ViewMode = ViewMode.Spread;
                break;
            case Key.R when !ctrl && !alt:
                vm.ToggleDirection();
                break;
            case Key.C when !ctrl && !alt:
                vm.ToggleCoverSingle();
                break;
            default:
                // JIS 配列などで「+」「;」が別のキーに割り当てられている場合
                if (ctrl && e.KeySymbol is "+" or ";" or "=")
                {
                    PageView.ZoomBy(1.25);
                }
                else if (ctrl && e.KeySymbol is "-")
                {
                    PageView.ZoomBy(1 / 1.25);
                }
                else
                {
                    handled = false;
                }
                break;
        }
        e.Handled = handled;
    }

    // ---- ファイルを開く（仕様 6.3） ----

    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "ファイルを開く",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("対応ファイル")
                {
                    Patterns = ["*.zip", "*.cbz", "*.pdf", .. ImageFormats.Extensions.Select(e => "*" + e)],
                },
                new FilePickerFileType("ZIP / CBZ") { Patterns = ["*.zip", "*.cbz"] },
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] },
                new FilePickerFileType("画像") { Patterns = ImageFormats.Extensions.Select(e => "*" + e).ToArray() },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && ViewModel is { } vm)
        {
            await vm.OpenAsync(path);
        }
    }

    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "画像フォルダを開く",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path && ViewModel is { } vm)
        {
            await vm.OpenAsync(path);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is not null && ViewModel is { } vm)
        {
            Activate();
            await vm.OpenAsync(path);
        }
    }

    // ---- メニュー ----

    private void OnOpenFileClick(object? sender, RoutedEventArgs e) => _ = OpenFileAsync();

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e) => _ = OpenFolderAsync();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => PageView.ZoomBy(1.25);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => PageView.ZoomBy(1 / 1.25);

    private void OnFullScreenClick(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    /// <summary>数値の選択肢をラジオ項目として並べ、サブメニューを開くたびに現在値のチェックを更新する。</summary>
    private static void BuildChoiceMenu(MenuItem parent, int[] choices, Func<int, string> label, Func<int?> current, Action<int> apply)
    {
        var items = choices.Select(v =>
        {
            var item = new MenuItem
            {
                Header = label(v),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = parent.Name,
                Tag = v,
            };
            item.Click += (_, _) => apply(v);
            return item;
        }).ToList();
        parent.ItemsSource = items;
        parent.SubmenuOpened += (_, _) =>
        {
            var value = current();
            foreach (var item in items)
            {
                item.IsChecked = (int)item.Tag! == value;
            }
        };
    }
}
