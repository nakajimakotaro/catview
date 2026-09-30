using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MangaViewer.Core.Persistence;
using MangaViewer.ViewModels;
using MangaViewer.Views;

namespace MangaViewer;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load(AppPaths.SettingsFile);
            var history = new HistoryStore(AppPaths.HistoryFile);
            // 起動時に上限を超えた古い記録を削除する（仕様 7.8）
            history.Prune();

            var viewModel = new MainViewModel(settings, history);
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => viewModel.Shutdown();

            // コマンドライン引数（MangaViewer <パス>）
            var initialPath = desktop.Args?.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
            if (initialPath is not null)
            {
                window.Opened += async (_, _) => await viewModel.OpenAsync(initialPath);
            }

            // macOS のファイル関連付けからの起動・Finder からのオープン
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += async (_, e) =>
                {
                    if (e is FileActivatedEventArgs { Files: { } files }
                        && files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null) is { } path)
                    {
                        await viewModel.OpenAsync(path);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
