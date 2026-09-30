using MangaViewer.Core.Imaging;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;

namespace MangaViewer.Core.Sources;

/// <summary>
/// PDFtoImage（PDFium）で各ページをレンダリングする。
/// PDFium はスレッドセーフではなく、PDFtoImage が内部で直列化するため、レンダリングは同時に 1 件のみ（仕様 11.2）。
/// 呼び出しごとの読み込みコストを抑えるため、PDF のバイト列はメモリに保持して使い回す（仕様 11.3）。
/// </summary>
public sealed class PdfPageSource : IPageSource
{
    /// <summary>ページサイズ（ポイント）を 96dpi 相当のピクセルに換算して「原寸」とする。</summary>
    private const double PointsToPixels = 96.0 / 72.0;

    /// <summary>ズーム時の過大なレンダリングを防ぐ上限（長辺ピクセル）。</summary>
    private const int MaxRenderDimension = 8192;

    private readonly byte[] _data;
    private readonly PageSize[] _sizes;

    public PdfPageSource(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _data = File.ReadAllBytes(Path);
        try
        {
            var sizes = Conversion.GetPageSizes(_data);
            _sizes = sizes
                .Select(s => new PageSize(
                    Math.Max(1, (int)Math.Round(s.Width * PointsToPixels)),
                    Math.Max(1, (int)Math.Round(s.Height * PointsToPixels))))
                .ToArray();
        }
        catch (PdfPasswordProtectedException)
        {
            throw new EncryptedFileException();
        }
        catch (PdfUnsupportedSecuritySchemeException)
        {
            throw new EncryptedFileException();
        }
        catch (PdfException e)
        {
            throw new PageSourceException("PDF ファイルを読み込めません", e);
        }
    }

    public string Path { get; }

    public int PageCount => _sizes.Length;

    public PageSize GetPageSize(int index) => _sizes[index];

    public string GetPageName(int index) => $"{index + 1} ページ";

    public Task<PageImage> GetPageAsync(int index, PageSize targetSize, CancellationToken ct)
    {
        var size = _sizes[index];
        var target = targetSize.IsEmpty ? size : targetSize;
        var scale = Math.Min(1.0, (double)MaxRenderDimension / Math.Max(target.Width, target.Height));
        var width = Math.Max(1, (int)(target.Width * scale));
        var height = Math.Max(1, (int)(target.Height * scale));

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var options = new RenderOptions(
                Width: width,
                Height: height,
                WithAspectRatio: true,
                WithAnnotations: true,
                BackgroundColor: SKColors.White);
            var bitmap = Conversion.ToImage(_data, index, options: options);
            // PDF は解像度を上げれば精細になるため、原寸扱いにはしない
            return PageImage.FromBitmap(bitmap, size, isFullResolution: false);
        }, ct);
    }

    public void Dispose()
    {
    }
}
