using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MangaViewer.Core;
using MangaViewer.Core.Caching;
using MangaViewer.Core.Layout;
using MangaViewer.Core.Persistence;
using MangaViewer.Core.Sources;

namespace MangaViewer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const string AppTitle = "MangaViewer";

    private readonly AppSettings _settings;
    private readonly HistoryStore _history;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _viewChangedTimer;

    private IPageSource? _source;
    private PageCache? _cache;
    private string? _recordPath;
    private PageSize[] _sizes = [];
    private IReadOnlyList<DisplayUnit> _units = [];
    private CancellationTokenSource? _loadCts;
    private int _openGeneration;
    private bool _suppressRelayout;
    private bool _updatingSeek;
    private bool _isShutDown;

    public MainViewModel(AppSettings settings, HistoryStore history)
    {
        _settings = settings;
        _history = history;
        _viewMode = settings.DefaultViewMode;
        _direction = settings.DefaultDirection;
        _coverSingle = settings.DefaultCoverSingle;
        _backgroundColor = Color.TryParse(settings.BackgroundColor, out var c) ? c : Colors.Black;

        // ページ移動時の記録は一定時間の遅延後にまとめて書き込む（仕様 7.6）
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _saveTimer.Tick += (_, _) => SaveRecord();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast = null;
        };
        _viewChangedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _viewChangedTimer.Tick += (_, _) =>
        {
            _viewChangedTimer.Stop();
            StartLoading();
        };
    }

    /// <summary>表示に必要なピクセルサイズを計算する（PageView から設定される）。</summary>
    public Func<IReadOnlyList<PageSize>, bool, PageSize[]>? TargetSizeProvider { get; set; }

    // ---- ファイルごとの表示設定 ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleMode), nameof(IsSpreadMode))]
    private ViewMode _viewMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRightToLeft), nameof(IsLeftToRight), nameof(IsSeekReversed))]
    private ReadingDirection _direction;

    [ObservableProperty]
    private bool _shift;

    [ObservableProperty]
    private bool _coverSingle;

    public bool IsSingleMode
    {
        get => ViewMode == ViewMode.Single;
        set { if (value) ViewMode = ViewMode.Single; }
    }

    public bool IsSpreadMode
    {
        get => ViewMode == ViewMode.Spread;
        set { if (value) ViewMode = ViewMode.Spread; }
    }

    public bool IsRightToLeft
    {
        get => Direction == ReadingDirection.RightToLeft;
        set { if (value) Direction = ReadingDirection.RightToLeft; }
    }

    public bool IsLeftToRight
    {
        get => Direction == ReadingDirection.LeftToRight;
        set { if (value) Direction = ReadingDirection.LeftToRight; }
    }

    partial void OnViewModeChanged(ViewMode value) => OnFileSettingChanged(relayout: true);

    partial void OnShiftChanged(bool value) => OnFileSettingChanged(relayout: true);

    partial void OnCoverSingleChanged(bool value) => OnFileSettingChanged(relayout: true);

    partial void OnDirectionChanged(ReadingDirection value) => OnFileSettingChanged(relayout: false);

    // ---- 全体設定 ----

    [ObservableProperty]
    private Color _backgroundColor;

    partial void OnBackgroundColorChanged(Color value)
    {
        _settings.BackgroundColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        SaveSettings();
    }

    public FitMode FitMode
    {
        get => _settings.FitMode;
        set
        {
            if (_settings.FitMode == value) return;
            _settings.FitMode = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFitWindow));
            OnPropertyChanged(nameof(IsFitWidth));
            OnPropertyChanged(nameof(IsFitHeight));
            OnPropertyChanged(nameof(IsFitOriginal));
        }
    }

    public bool IsFitWindow { get => FitMode == FitMode.Window; set { if (value) FitMode = FitMode.Window; } }

    public bool IsFitWidth { get => FitMode == FitMode.Width; set { if (value) FitMode = FitMode.Width; } }

    public bool IsFitHeight { get => FitMode == FitMode.Height; set { if (value) FitMode = FitMode.Height; } }

    public bool IsFitOriginal { get => FitMode == FitMode.Original; set { if (value) FitMode = FitMode.Original; } }

    public InterpolationQuality Interpolation
    {
        get => _settings.Interpolation;
        set
        {
            if (_settings.Interpolation == value) return;
            _settings.Interpolation = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInterpolationLow));
            OnPropertyChanged(nameof(IsInterpolationMedium));
            OnPropertyChanged(nameof(IsInterpolationHigh));
        }
    }

    public bool IsInterpolationLow { get => Interpolation == InterpolationQuality.Low; set { if (value) Interpolation = InterpolationQuality.Low; } }

    public bool IsInterpolationMedium { get => Interpolation == InterpolationQuality.Medium; set { if (value) Interpolation = InterpolationQuality.Medium; } }

    public bool IsInterpolationHigh { get => Interpolation == InterpolationQuality.High; set { if (value) Interpolation = InterpolationQuality.High; } }

    public bool AutoSingleLandscape
    {
        get => _settings.AutoSingleLandscape;
        set
        {
            if (_settings.AutoSingleLandscape == value) return;
            _settings.AutoSingleLandscape = value;
            SaveSettings();
            OnPropertyChanged();
            RelayoutKeepingPosition();
        }
    }

    public int PrefetchCount
    {
        get => _settings.PrefetchCount;
        set
        {
            if (_settings.PrefetchCount == value) return;
            _settings.PrefetchCount = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public int CacheLimitMB
    {
        get => _settings.CacheLimitMB;
        set
        {
            if (_settings.CacheLimitMB == value) return;
            _settings.CacheLimitMB = value;
            if (_cache is not null) _cache.LimitBytes = value * 1024L * 1024;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool IsDefaultSingle
    {
        get => _settings.DefaultViewMode == ViewMode.Single;
        set { if (value) SetDefault(() => _settings.DefaultViewMode = ViewMode.Single); }
    }

    public bool IsDefaultSpread
    {
        get => _settings.DefaultViewMode == ViewMode.Spread;
        set { if (value) SetDefault(() => _settings.DefaultViewMode = ViewMode.Spread); }
    }

    public bool IsDefaultRightToLeft
    {
        get => _settings.DefaultDirection == ReadingDirection.RightToLeft;
        set { if (value) SetDefault(() => _settings.DefaultDirection = ReadingDirection.RightToLeft); }
    }

    public bool IsDefaultLeftToRight
    {
        get => _settings.DefaultDirection == ReadingDirection.LeftToRight;
        set { if (value) SetDefault(() => _settings.DefaultDirection = ReadingDirection.LeftToRight); }
    }

    public bool DefaultCoverSingle
    {
        get => _settings.DefaultCoverSingle;
        set => SetDefault(() => _settings.DefaultCoverSingle = value);
    }

    public WindowPlacement? WindowPlacement
    {
        get => _settings.Window;
        set => _settings.Window = value;
    }

    private void SetDefault(Action apply)
    {
        apply();
        SaveSettings();
        OnPropertyChanged(nameof(IsDefaultSingle));
        OnPropertyChanged(nameof(IsDefaultSpread));
        OnPropertyChanged(nameof(IsDefaultRightToLeft));
        OnPropertyChanged(nameof(IsDefaultLeftToRight));
        OnPropertyChanged(nameof(DefaultCoverSingle));
    }

    // ---- 表示状態 ----

    [ObservableProperty]
    private IReadOnlyList<DisplayedPage> _displayedPages = [];

    [ObservableProperty]
    private string _title = AppTitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message = "ファイルをドラッグ&ドロップするか、Ctrl+O で開いてください";

    public bool HasMessage => Message is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToast))]
    private string? _toast;

    public bool HasToast => Toast is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChromeVisible))]
    private bool _isFullScreen;

    public bool IsChromeVisible => !IsFullScreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    private int _pageCount;

    public bool HasDocument => PageCount > 0;

    [ObservableProperty]
    private string _pageText = "";

    /// <summary>ページシークバーの値（1 始まりのページ番号）。</summary>
    [ObservableProperty]
    private double _seekPage = 1;

    public bool IsSeekReversed => Direction == ReadingDirection.RightToLeft;

    partial void OnSeekPageChanged(double value)
    {
        if (_updatingSeek || _units.Count == 0) return;
        var page = Math.Clamp((int)Math.Round(value) - 1, 0, PageCount - 1);
        var index = SpreadLayout.FindUnitIndex(_units, page);
        if (index != CurrentUnitIndex) ShowUnit(index);
    }

    public int CurrentUnitIndex { get; private set; } = -1;

    private DisplayUnit? CurrentUnit =>
        CurrentUnitIndex >= 0 && CurrentUnitIndex < _units.Count ? _units[CurrentUnitIndex] : null;

    // ---- ファイル操作 ----

    public async Task OpenAsync(string path)
    {
        var generation = ++_openGeneration;
        SaveRecord();

        OpenedSource opened;
        try
        {
            opened = await Task.Run(() => PageSourceFactory.Open(path));
        }
        catch (PageSourceException e)
        {
            if (generation == _openGeneration) ShowOpenError(e.Message);
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            if (generation == _openGeneration) ShowOpenError($"ファイルを開けません: {e.Message}");
            return;
        }

        if (generation != _openGeneration)
        {
            opened.Source.Dispose();
            return;
        }
        if (opened.Source.PageCount == 0)
        {
            opened.Source.Dispose();
            ShowOpenError("表示できるページがありません");
            return;
        }

        CloseCurrent();
        var source = opened.Source;
        _source = source;
        _cache = new PageCache(source, CacheLimitMB * 1024L * 1024);
        _recordPath = AppPaths.NormalizePath(source.Path);
        _sizes = Enumerable.Range(0, source.PageCount).Select(source.GetPageSize).ToArray();
        PageCount = source.PageCount;

        // ファイルごとの記録があればそれを、なければ全体のデフォルト設定を使う（仕様 7.4）
        var record = _history.Get(_recordPath);
        var page = 0;
        _suppressRelayout = true;
        if (record is not null)
        {
            ViewMode = record.ViewMode;
            Direction = record.Direction;
            Shift = record.Shift;
            CoverSingle = record.CoverSingle;
            // 総ページ数以上なら先頭から。読み終えたファイルも記録どおり最終ページから表示する（仕様 7.5）
            page = record.LastPage < source.PageCount && record.LastPage >= 0 ? record.LastPage : 0;
        }
        else
        {
            ViewMode = _settings.DefaultViewMode;
            Direction = _settings.DefaultDirection;
            Shift = false;
            CoverSingle = _settings.DefaultCoverSingle;
        }
        _suppressRelayout = false;
        if (opened.InitialPage is { } initial)
        {
            page = initial;
        }

        Title = $"{Path.GetFileName(source.Path)} - {AppTitle}";
        Message = null;
        Relayout(page);
        SaveRecord();
    }

    private void ShowOpenError(string message)
    {
        if (_source is null)
        {
            Message = message;
        }
        else
        {
            ShowToast(message);
        }
    }

    [RelayCommand]
    public void CloseFile()
    {
        _openGeneration++;
        SaveRecord();
        CloseCurrent();
        Title = AppTitle;
        Message = "ファイルをドラッグ&ドロップするか、Ctrl+O で開いてください";
    }

    private void CloseCurrent()
    {
        _saveTimer.Stop();
        _loadCts?.Cancel();
        _loadCts = null;
        SetDisplayedPages([]);
        _cache?.Dispose();
        _cache = null;
        _source?.Dispose();
        _source = null;
        _recordPath = null;
        _sizes = [];
        _units = [];
        CurrentUnitIndex = -1;
        PageCount = 0;
        PageText = "";
    }

    [RelayCommand]
    public Task NextFile() => OpenSiblingAsync(1);

    [RelayCommand]
    public Task PreviousFile() => OpenSiblingAsync(-1);

    private async Task OpenSiblingAsync(int delta)
    {
        if (_source is null) return;
        var sibling = FileNavigator.FindSibling(_source.Path, delta);
        if (sibling is null)
        {
            ShowToast(delta > 0 ? "次のファイルはありません" : "前のファイルはありません");
            return;
        }
        await OpenAsync(sibling);
    }

    public void Shutdown()
    {
        if (_isShutDown) return;
        _isShutDown = true;
        SaveRecord();
        SaveSettings();
        CloseCurrent();
        _history.Dispose();
    }

    // ---- ページ移動 ----

    [RelayCommand]
    public void NextPage()
    {
        if (CurrentUnitIndex < _units.Count - 1) ShowUnit(CurrentUnitIndex + 1);
    }

    [RelayCommand]
    public void PreviousPage()
    {
        if (CurrentUnitIndex > 0) ShowUnit(CurrentUnitIndex - 1);
    }

    /// <summary>表示単位で移動する。+1 が次、-1 が前。</summary>
    public void StepUnit(int delta)
    {
        if (delta > 0) NextPage();
        else if (delta < 0) PreviousPage();
    }

    [RelayCommand]
    public void FirstPage()
    {
        if (_units.Count > 0) ShowUnit(0);
    }

    [RelayCommand]
    public void LastPage()
    {
        if (_units.Count > 0) ShowUnit(_units.Count - 1);
    }

    /// <summary>
    /// 見開き時に 1 ページだけ進む / 戻る。
    /// レイアウトは先頭から組み立てる規則（仕様 5 章）を保つため、1ページずらしを切り替えて
    /// 表示位置が 1 ページずれた表示単位へ移動する。切り替えても 1 ページずれない場合（横長ページ付近など）は
    /// 通常のページ移動と同じく隣の表示単位へ移動する。
    /// </summary>
    public void StepPage(int delta)
    {
        if (CurrentUnit is not { } unit) return;
        var target = unit.Start + delta;
        if (target < 0 || target >= PageCount) return;
        if (ViewMode == ViewMode.Single)
        {
            ShowUnit(target);
            return;
        }

        var shifted = SpreadLayout.Compute(PageCount, LandscapeFlags(), ViewMode, CoverSingle, !Shift);
        var shiftedIndex = SpreadLayout.FindUnitIndex(shifted, target);
        if (shifted[shiftedIndex].Start == target)
        {
            _suppressRelayout = true;
            Shift = !Shift;
            _suppressRelayout = false;
            _units = shifted;
            ShowUnit(shiftedIndex);
            SaveRecord();
        }
        else
        {
            ShowUnit(SpreadLayout.FindUnitIndex(_units, target));
        }
    }

    [RelayCommand]
    public void ToggleShift() => Shift = !Shift;

    [RelayCommand]
    public void ToggleCoverSingle() => CoverSingle = !CoverSingle;

    [RelayCommand]
    public void ToggleDirection() =>
        Direction = Direction == ReadingDirection.RightToLeft ? ReadingDirection.LeftToRight : ReadingDirection.RightToLeft;

    [RelayCommand]
    public void SetBackground(string color)
    {
        if (Color.TryParse(color, out var c)) BackgroundColor = c;
    }

    public void SetPrefetchCount(int count) => PrefetchCount = count;

    public void SetCacheLimit(int megabytes) => CacheLimitMB = megabytes;

    private void OnFileSettingChanged(bool relayout)
    {
        if (_suppressRelayout || _source is null) return;
        if (relayout) RelayoutKeepingPosition();
        // 表示設定の変更時はすぐに記録する
        SaveRecord();
    }

    /// <summary>切り替え前に表示していたページのうち最も若い番号のページを含む表示単位を表示する（仕様 5.3）。</summary>
    private void RelayoutKeepingPosition()
    {
        if (CurrentUnit is { } unit) Relayout(unit.Start);
    }

    private bool[] LandscapeFlags() =>
        _settings.AutoSingleLandscape ? _sizes.Select(s => s.IsLandscape).ToArray() : new bool[_sizes.Length];

    private void Relayout(int anchorPage)
    {
        if (_source is null) return;
        _units = SpreadLayout.Compute(PageCount, LandscapeFlags(), ViewMode, CoverSingle, Shift);
        ShowUnit(SpreadLayout.FindUnitIndex(_units, anchorPage), force: true);
    }

    private void ShowUnit(int index, bool force = false)
    {
        if (_cache is null || _units.Count == 0) return;
        index = Math.Clamp(index, 0, _units.Count - 1);
        if (index == CurrentUnitIndex && !force && DisplayedPages.Count > 0 && DisplayedPages[0].Index == _units[index].Start)
        {
            return;
        }
        CurrentUnitIndex = index;
        var unit = _units[index];
        _cache.SetPosition(unit.Start);

        PageText = unit.Count == 1
            ? $"{unit.Start + 1} / {PageCount}"
            : $"{unit.Start + 1}-{unit.End + 1} / {PageCount}";
        _updatingSeek = true;
        SeekPage = unit.Start + 1;
        _updatingSeek = false;

        // キャッシュ済みのページはすぐ表示し、残りは読み込み後に差し替える
        var sizes = unit.Pages.Select(p => _sizes[p]).ToArray();
        var targets = Targets(sizes, useCurrentZoom: false);
        SetDisplayedPages(unit.Pages
            .Select((p, i) => new DisplayedPage(p, _sizes[p], _cache.TryGet(p, targets[i])))
            .ToList());

        _saveTimer.Stop();
        _saveTimer.Start();
        StartLoading();
    }

    /// <summary>表示領域やズームが変わったときに呼ばれる。必要なら高解像度で読み込み直す。</summary>
    public void OnViewChanged()
    {
        if (_source is null) return;
        _viewChangedTimer.Stop();
        _viewChangedTimer.Start();
    }

    private PageSize[] Targets(IReadOnlyList<PageSize> sizes, bool useCurrentZoom) =>
        TargetSizeProvider?.Invoke(sizes, useCurrentZoom) ?? sizes.ToArray();

    /// <summary>
    /// 現在の表示単位を最優先で読み込み、その後に前後のページを順番に先読みする（仕様 9 章）。
    /// 新しい移動があれば、まだ始まっていない読み込みはキャンセルされる。
    /// </summary>
    private async void StartLoading()
    {
        _loadCts?.Cancel();
        if (_cache is not { } cache || CurrentUnit is not { } unit) return;
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        try
        {
            var sizes = unit.Pages.Select(p => _sizes[p]).ToArray();
            var targets = Targets(sizes, useCurrentZoom: true);
            var loads = unit.Pages.Select((p, i) => LoadDisplayedAsync(cache, p, targets[i], ct)).ToArray();
            await Task.WhenAll(loads);

            foreach (var (page, target) in PrefetchOrder(unit))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var image = await cache.GetAsync(page, target, ct);
                    if (image is null) break; // キャッシュ上限に達した
                    image.Release();
                }
                catch (Exception e) when (e is not OperationCanceledException and not ObjectDisposedException)
                {
                    // 先読みの失敗は表示時に改めて扱う
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
            // 読み込み中にファイルが閉じられた
        }
    }

    private async Task LoadDisplayedAsync(PageCache cache, int page, PageSize target, CancellationToken ct)
    {
        var existing = DisplayedPages.FirstOrDefault(d => d.Index == page);
        if (existing?.Image is { } img && img.Satisfies(target)) return;

        DisplayedPage replacement;
        try
        {
            var image = await cache.GetAsync(page, target, ct);
            if (image is null) return;
            replacement = new DisplayedPage(page, _sizes[page], image);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception e)
        {
            if (existing?.Image is not null) return;
            var name = _source?.GetPageName(page) ?? "";
            replacement = new DisplayedPage(page, _sizes[page], null, $"{name}\n表示できません ({e.Message})");
        }

        if (ct.IsCancellationRequested || DisplayedPages.All(d => d.Index != page))
        {
            replacement.Image?.Release();
            ct.ThrowIfCancellationRequested();
            return;
        }
        var pages = DisplayedPages
            .Select(d => d.Index == page ? replacement : d with { Image = d.Image?.AddRef() })
            .ToList();
        SetDisplayedPages(pages);
    }

    private IEnumerable<(int Page, PageSize Target)> PrefetchOrder(DisplayUnit current)
    {
        var count = PrefetchCount;
        if (count <= 0) yield break;

        // 次方向を優先し、戻る方向はその半分だけ先読みする
        var forward = 0;
        for (var u = CurrentUnitIndex + 1; u < _units.Count && forward < count; u++)
        {
            foreach (var item in UnitTargets(_units[u])) yield return item;
            forward += _units[u].Count;
        }
        var backward = 0;
        for (var u = CurrentUnitIndex - 1; u >= 0 && backward < (count + 1) / 2; u--)
        {
            foreach (var item in UnitTargets(_units[u])) yield return item;
            backward += _units[u].Count;
        }
    }

    private IEnumerable<(int, PageSize)> UnitTargets(DisplayUnit unit)
    {
        var sizes = unit.Pages.Select(p => _sizes[p]).ToArray();
        var targets = Targets(sizes, useCurrentZoom: false);
        return unit.Pages.Select((p, i) => (p, targets[i]));
    }

    private void SetDisplayedPages(IReadOnlyList<DisplayedPage> pages)
    {
        var old = DisplayedPages;
        DisplayedPages = pages;
        foreach (var page in old)
        {
            page.Image?.Release();
        }
    }

    // ---- 記録 ----

    private void SaveRecord()
    {
        _saveTimer.Stop();
        if (_recordPath is null || CurrentUnit is not { } unit) return;
        try
        {
            _history.Save(new HistoryRecord(
                _recordPath,
                unit.Start,
                PageCount,
                ViewMode,
                Direction,
                Shift,
                CoverSingle,
                DateTimeOffset.Now));
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            ShowToast($"閲覧記録を保存できません: {e.Message}");
        }
    }

    public void SaveSettings()
    {
        try
        {
            _settings.Save(AppPaths.SettingsFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowToast($"設定を保存できません: {e.Message}");
        }
    }

    public void ShowToast(string text)
    {
        Toast = text;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
