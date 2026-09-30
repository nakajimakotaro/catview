using MangaViewer.Core.Imaging;

namespace MangaViewer.Core.Sources;

/// <summary>PDF / ZIP / 画像フォルダの差異を吸収するページソース（仕様 8.2）。</summary>
public interface IPageSource : IDisposable
{
    /// <summary>ソースのパス（ZIP / PDF ファイル、または画像フォルダ）。</summary>
    string Path { get; }

    int PageCount { get; }

    /// <summary>デコードせずに取得できるページサイズ（横長判定用）。不明な場合は <see cref="PageSize.Empty"/>。</summary>
    PageSize GetPageSize(int index);

    /// <summary>
    /// 指定サイズに合わせてページ画像を取得する。PDF は targetSize に応じた解像度でレンダリングする。
    /// 返される画像の参照は呼び出し側が所有する。
    /// </summary>
    Task<PageImage> GetPageAsync(int index, PageSize targetSize, CancellationToken ct);

    /// <summary>ページの表示名（エラー表示用）。</summary>
    string GetPageName(int index);
}
