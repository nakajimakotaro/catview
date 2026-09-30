using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using MangaViewer.Core;
using MangaViewer.Core.Imaging;
using MangaViewer.ViewModels;
using SkiaSharp;

namespace MangaViewer.Controls;

/// <summary>
/// 表示単位（1 ページまたは見開き）を描画するコントロール。
/// フィット・ズーム・パンと、クリック領域・ホイールによるページ送りの入力を扱う。
/// </summary>
public sealed class PageView : Control
{
    public static readonly StyledProperty<IReadOnlyList<DisplayedPage>?> PagesProperty =
        AvaloniaProperty.Register<PageView, IReadOnlyList<DisplayedPage>?>(nameof(Pages));

    public static readonly StyledProperty<ReadingDirection> DirectionProperty =
        AvaloniaProperty.Register<PageView, ReadingDirection>(nameof(Direction));

    public static readonly StyledProperty<FitMode> FitModeProperty =
        AvaloniaProperty.Register<PageView, FitMode>(nameof(FitMode));

    public static readonly StyledProperty<Color> BackgroundColorProperty =
        AvaloniaProperty.Register<PageView, Color>(nameof(BackgroundColor), Colors.Black);

    public static readonly StyledProperty<InterpolationQuality> InterpolationProperty =
        AvaloniaProperty.Register<PageView, InterpolationQuality>(nameof(Interpolation), InterpolationQuality.High);

    private const double MinZoom = 0.1;
    private const double MaxZoom = 16;
    private const double DragThreshold = 4;

    /// <summary>サイズ不明のページは一般的な漫画の縦横比として扱う。</summary>
    private static readonly PageSize FallbackSize = new(1000, 1414);

    private double _zoom = 1;
    private Vector _offset;
    private Point? _pressPoint;
    private Vector _pressOffset;
    private bool _dragging;
    private double _wheelAccumulator;
    private int? _shownFirstPage;

    static PageView()
    {
        AffectsRender<PageView>(PagesProperty, DirectionProperty, FitModeProperty, BackgroundColorProperty, InterpolationProperty);
        FocusableProperty.OverrideDefaultValue<PageView>(true);
        ClipToBoundsProperty.OverrideDefaultValue<PageView>(true);
    }

    public IReadOnlyList<DisplayedPage>? Pages
    {
        get => GetValue(PagesProperty);
        set => SetValue(PagesProperty, value);
    }

    public ReadingDirection Direction
    {
        get => GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    public FitMode FitMode
    {
        get => GetValue(FitModeProperty);
        set => SetValue(FitModeProperty, value);
    }

    public Color BackgroundColor
    {
        get => GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    public InterpolationQuality Interpolation
    {
        get => GetValue(InterpolationProperty);
        set => SetValue(InterpolationProperty, value);
    }

    /// <summary>ページ送りの要求。+1 が次、-1 が前。</summary>
    public event Action<int>? NavigateRequested;

    /// <summary>画面中央のダブルクリック。</summary>
    public event Action? ToggleFullScreenRequested;

    /// <summary>ズームや表示領域のサイズが変わり、必要な画像解像度が変わった可能性がある。</summary>
    public event Action? ViewChanged;

    public double Zoom => _zoom;

    private double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    /// <summary>ズームとパンを初期状態に戻す。</summary>
    public void ResetView()
    {
        var changed = _zoom != 1;
        _zoom = 1;
        _offset = InitialOffset(ComputeContentSize());
        InvalidateVisual();
        if (changed) ViewChanged?.Invoke();
    }

    public void ZoomBy(double factor, Point? anchor = null)
    {
        var newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (newZoom == _zoom) return;
        var center = anchor ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        // アンカー位置のコンテンツ上の点が動かないようにオフセットを補正する
        var ratio = newZoom / _zoom;
        _offset = new Vector(
            center.X - (center.X - _offset.X) * ratio,
            center.Y - (center.Y - _offset.Y) * ratio);
        _zoom = newZoom;
        _offset = ClampOffset(_offset, ComputeContentSize());
        UpdateCursor();
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>
    /// 各ページを表示するのに必要なピクセルサイズを計算する。
    /// useCurrentZoom が false の場合はズームなし（先読み用）で計算する。
    /// </summary>
    public PageSize[] ComputeTargetSizes(IReadOnlyList<PageSize> sizes, bool useCurrentZoom)
    {
        var geometry = ComputeGeometry(Bounds.Size, sizes, useCurrentZoom ? _zoom : 1);
        var scaling = RenderScaling;
        return geometry.PageSizes
            .Select(s => new PageSize((int)Math.Ceiling(s.Width * scaling), (int)Math.Ceiling(s.Height * scaling)))
            .ToArray();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PagesProperty)
        {
            var first = Pages is { Count: > 0 } p ? p[0].Index : (int?)null;
            if (first != _shownFirstPage)
            {
                _shownFirstPage = first;
                ResetView();
            }
            else
            {
                _offset = ClampOffset(_offset, ComputeContentSize());
            }
            UpdateCursor();
        }
        else if (change.Property == FitModeProperty || change.Property == DirectionProperty)
        {
            ResetView();
            UpdateCursor();
            ViewChanged?.Invoke();
        }
        else if (change.Property == BoundsProperty)
        {
            var oldSize = ((Rect)change.OldValue!).Size;
            var newSize = ((Rect)change.NewValue!).Size;
            if (oldSize != newSize)
            {
                _offset = oldSize == default ? InitialOffset(ComputeContentSize()) : ClampOffset(_offset, ComputeContentSize());
                UpdateCursor();
                ViewChanged?.Invoke();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(BackgroundColor), bounds);

        var pages = Pages;
        if (pages is null || pages.Count == 0) return;

        var geometry = ComputeGeometry(Bounds.Size, pages.Select(p => p.Size).ToArray(), _zoom);
        var scaling = RenderScaling;
        for (var i = 0; i < pages.Count; i++)
        {
            var rect = geometry.PageRects[i].Translate(_offset);
            var page = pages[i];
            if (page.Image is { } image)
            {
                if (rect.Intersects(bounds))
                {
                    context.Custom(new ImageDrawOperation(rect, bounds, image.AddRef(), SamplingFor(image, rect, scaling)));
                }
            }
            else
            {
                DrawPlaceholder(context, rect, page.Error ?? "読み込み中…");
            }
        }
    }

    private void DrawPlaceholder(DrawingContext context, Rect rect, string text)
    {
        var bg = BackgroundColor;
        var isDark = (bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114) < 128;
        var fg = isDark ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(160, 0, 0, 0);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(48, fg.R, fg.G, fg.B))), rect.Deflate(0.5));
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Typeface.Default, 14, new SolidColorBrush(fg))
        {
            MaxTextWidth = Math.Max(1, rect.Width - 16),
            TextAlignment = TextAlignment.Center,
        };
        context.DrawText(formatted, new Point(rect.X + 8, rect.Center.Y - formatted.Height / 2));
    }

    private SKSamplingOptions SamplingFor(PageImage image, Rect dest, double scaling)
    {
        switch (Interpolation)
        {
            case InterpolationQuality.Low:
                return new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            case InterpolationQuality.Medium:
                return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            default:
                // 縮小時はミップマップで線のちらつき（エイリアシング）を抑え、拡大時はバイキュービックで補間する
                var downscale = dest.Width * scaling < image.Width;
                return downscale
                    ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                    : new SKSamplingOptions(SKCubicResampler.Mitchell);
        }
    }

    // ---- 入力 ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus();

        var region = RegionAt(point.Position.X);
        if (e.ClickCount == 2 && region == 0)
        {
            ToggleFullScreenRequested?.Invoke();
            _pressPoint = null;
            e.Handled = true;
            return;
        }
        _pressPoint = point.Position;
        _pressOffset = _offset;
        _dragging = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressPoint is not { } start) return;
        var pos = e.GetPosition(this);
        var delta = pos - start;
        if (!_dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) > DragThreshold && CanPan())
        {
            _dragging = true;
        }
        if (_dragging)
        {
            _offset = ClampOffset(_pressOffset + delta, ComputeContentSize());
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressPoint is { } start && !_dragging && e.InitialPressMouseButton == MouseButton.Left)
        {
            var region = RegionAt(start.X);
            if (region != 0)
            {
                // 左領域は右読みなら次、左読みなら前
                var leftIsNext = Direction == ReadingDirection.RightToLeft;
                NavigateRequested?.Invoke((region < 0) == leftIsNext ? 1 : -1);
            }
        }
        _pressPoint = null;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
        var modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta))
        {
            ZoomBy(Math.Pow(1.15, e.Delta.Y), e.GetPosition(this));
            return;
        }

        var content = ComputeContentSize();
        var overflowY = content.Height > Bounds.Height + 0.5;
        var overflowX = content.Width > Bounds.Width + 0.5;
        if (overflowY || overflowX)
        {
            // ズーム中・幅合わせ等ではスクロールし、端に達していればページ送りする
            var before = _offset;
            var step = 80 * e.Delta.Y;
            Vector move;
            if (overflowY)
            {
                move = new Vector(-80 * e.Delta.X, step);
            }
            else
            {
                // 横方向のみはみ出す場合は、読み進める向きへスクロールする
                move = new Vector(Direction == ReadingDirection.RightToLeft ? -step : step, 0);
            }
            _offset = ClampOffset(_offset + move, content);
            if (_offset != before)
            {
                _wheelAccumulator = 0;
                InvalidateVisual();
                return;
            }
        }

        _wheelAccumulator += e.Delta.Y;
        if (Math.Abs(_wheelAccumulator) >= 1)
        {
            NavigateRequested?.Invoke(_wheelAccumulator < 0 ? 1 : -1);
            _wheelAccumulator = 0;
        }
    }

    /// <summary>-1: 左領域、0: 中央、1: 右領域。</summary>
    private int RegionAt(double x)
    {
        var w = Bounds.Width;
        if (x < w / 3) return -1;
        if (x > w * 2 / 3) return 1;
        return 0;
    }

    private bool CanPan()
    {
        var content = ComputeContentSize();
        return content.Width > Bounds.Width + 0.5 || content.Height > Bounds.Height + 0.5;
    }

    private void UpdateCursor()
    {
        Cursor = CanPan() ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
    }

    // ---- 配置計算 ----

    private readonly record struct Geometry(Size ContentSize, Rect[] PageRects, Size[] PageSizes);

    private Size ComputeContentSize()
    {
        var pages = Pages;
        if (pages is null || pages.Count == 0) return default;
        return ComputeGeometry(Bounds.Size, pages.Select(p => p.Size).ToArray(), _zoom).ContentSize;
    }

    /// <summary>
    /// 見開きの各ページを同じ高さに揃えて並べ、フィットモードとズームから表示サイズを決める。
    /// 返す矩形はコンテンツ左上を原点とし、読み方向に応じて左右を並べ替えたもの（入力と同じ順序）。
    /// </summary>
    private Geometry ComputeGeometry(Size viewport, IReadOnlyList<PageSize> sizes, double zoom)
    {
        var count = sizes.Count;
        if (count == 0) return new Geometry(default, [], []);

        var normalized = sizes.Select(s => s.IsEmpty ? FallbackSize : s).ToArray();
        double refHeight = normalized.Max(s => s.Height);
        var widths = normalized.Select(s => s.Width * refHeight / s.Height).ToArray();
        var contentWidth = widths.Sum();

        var vw = Math.Max(1, viewport.Width);
        var vh = Math.Max(1, viewport.Height);
        var fit = FitMode switch
        {
            FitMode.Width => vw / contentWidth,
            FitMode.Height => vh / refHeight,
            FitMode.Original => 1 / RenderScaling,
            _ => Math.Min(vw / contentWidth, vh / refHeight),
        };
        var scale = fit * zoom;

        var rects = new Rect[count];
        var pageSizes = new Size[count];
        var x = 0.0;
        // 右読みでは若い番号を右に置くため、逆順に左から並べる
        var order = Direction == ReadingDirection.RightToLeft
            ? Enumerable.Range(0, count).Reverse()
            : Enumerable.Range(0, count);
        foreach (var i in order)
        {
            var size = new Size(widths[i] * scale, refHeight * scale);
            rects[i] = new Rect(new Point(x, 0), size);
            pageSizes[i] = size;
            x += size.Width;
        }
        return new Geometry(new Size(contentWidth * scale, refHeight * scale), rects, pageSizes);
    }

    private Vector InitialOffset(Size content)
    {
        var vw = Bounds.Width;
        // はみ出す場合は上端から、右読みなら右端・左読みなら左端から表示する
        var x = content.Width <= vw
            ? (vw - content.Width) / 2
            : Direction == ReadingDirection.RightToLeft ? vw - content.Width : 0;
        var y = content.Height <= Bounds.Height ? (Bounds.Height - content.Height) / 2 : 0;
        return new Vector(x, y);
    }

    private Vector ClampOffset(Vector offset, Size content)
    {
        static double Clamp(double value, double contentLength, double viewportLength) =>
            contentLength <= viewportLength
                ? (viewportLength - contentLength) / 2
                : Math.Clamp(value, viewportLength - contentLength, 0);

        return new Vector(Clamp(offset.X, content.Width, Bounds.Width), Clamp(offset.Y, content.Height, Bounds.Height));
    }

    /// <summary>SkiaSharp で直接ページ画像を描画する。画像の参照を保持し、破棄時に解放する。</summary>
    private sealed class ImageDrawOperation(Rect dest, Rect clip, PageImage image, SKSamplingOptions sampling) : ICustomDrawOperation
    {
        private PageImage? _image = image;

        public Rect Bounds { get; } = dest.Intersect(clip);

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            if (_image is null) return;
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null) return;
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            using var paint = new SKPaint();
            canvas.DrawImage(_image.Image, new SKRect((float)dest.Left, (float)dest.Top, (float)dest.Right, (float)dest.Bottom), sampling, paint);
        }

        public void Dispose()
        {
            _image?.Release();
            _image = null;
        }
    }
}
