using MangaViewer.Core;
using MangaViewer.Core.Imaging;

namespace MangaViewer.ViewModels;

/// <summary>
/// 表示中のページ。Image の参照はこのオブジェクトが 1 つ所有し、
/// 表示から外れたときに <see cref="MainViewModel"/> が解放する。
/// </summary>
public sealed record DisplayedPage(int Index, PageSize Size, PageImage? Image, string? Error = null);
