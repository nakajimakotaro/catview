namespace MangaViewer.Core;

public enum ViewMode
{
    Single = 0,
    Spread = 1,
}

public enum ReadingDirection
{
    /// <summary>右読み（右綴じ）。若い番号のページを右に配置し、次ページは左方向。</summary>
    RightToLeft = 0,

    /// <summary>左読み（左綴じ）。若い番号のページを左に配置し、次ページは右方向。</summary>
    LeftToRight = 1,
}

public enum FitMode
{
    Window = 0,
    Width = 1,
    Height = 2,
    Original = 3,
}

public enum InterpolationQuality
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>ページのピクセルサイズ。サイズ不明の場合は 0。</summary>
public readonly record struct PageSize(int Width, int Height)
{
    public static readonly PageSize Empty = new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>幅が高さより大きい（元から見開きになっている）ページか。</summary>
    public bool IsLandscape => !IsEmpty && Width > Height;
}
