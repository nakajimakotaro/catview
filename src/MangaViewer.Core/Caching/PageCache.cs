using MangaViewer.Core.Imaging;
using MangaViewer.Core.Sources;

namespace MangaViewer.Core.Caching;

/// <summary>
/// ページ画像のキャッシュ（仕様 9 章）。
/// メモリ使用量が上限を超えた場合は、現在位置から遠いページから破棄する。
/// 読み込みのキャンセルは「まだ始まっていなければ始めない」扱いとし、
/// 始まった読み込み（特に中断できない PDF のレンダリング）の結果は捨てずにキャッシュへ入れる。
/// </summary>
public sealed class PageCache : IDisposable
{
    private readonly IPageSource _source;
    private readonly Lock _lock = new();
    private readonly Dictionary<int, PageImage> _entries = new();
    private readonly Dictionary<int, (Task<PageImage> Task, PageSize Target)> _inflight = new();
    private long _totalBytes;
    private int _position;
    private bool _disposed;

    public PageCache(IPageSource source, long limitBytes)
    {
        _source = source;
        LimitBytes = limitBytes;
    }

    public long LimitBytes { get; set; }

    /// <summary>現在の表示位置を設定する。破棄の優先度の基準になる。</summary>
    public void SetPosition(int page)
    {
        lock (_lock)
        {
            _position = page;
            EvictIfNeeded();
        }
    }

    public bool Contains(int index)
    {
        lock (_lock)
        {
            return _entries.ContainsKey(index);
        }
    }

    /// <summary>要求サイズを満たすキャッシュ済み画像があれば、参照を追加して返す。</summary>
    public PageImage? TryGet(int index, PageSize target)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(index, out var img) && img.Satisfies(target) ? img.AddRef() : null;
        }
    }

    /// <summary>
    /// ページ画像を取得する。戻り値の参照は呼び出し側が所有し、不要になったら Release する。
    /// 読み込み直後に上限超過で破棄された場合は null を返す（先読みの打ち切り判定に使う）。
    /// </summary>
    public async Task<PageImage?> GetAsync(int index, PageSize target, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Task<PageImage> task;
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_entries.TryGetValue(index, out var cached) && cached.Satisfies(target))
                {
                    return cached.AddRef();
                }
                if (_inflight.TryGetValue(index, out var running) && Covers(running.Target, target))
                {
                    task = running.Task;
                }
                else
                {
                    task = LoadAsync(index, target, ct);
                    if (!task.IsCompleted)
                    {
                        _inflight[index] = (task, target);
                    }
                }
            }

            PageImage image;
            try
            {
                image = await task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 他の要求者のトークンで開始前にキャンセルされた読み込みだった。やり直す
                continue;
            }

            lock (_lock)
            {
                if (_entries.TryGetValue(index, out var current) && current == image)
                {
                    return image.AddRef();
                }
                if (current is not null && current.Satisfies(target))
                {
                    return current.AddRef();
                }
            }
            return null;
        }
        return null;
    }

    private async Task<PageImage> LoadAsync(int index, PageSize target, CancellationToken ct)
    {
        try
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            var image = await _source.GetPageAsync(index, target, ct).ConfigureAwait(false);
            lock (_lock)
            {
                if (_disposed)
                {
                    image.Release();
                    throw new ObjectDisposedException(nameof(PageCache));
                }
                if (_entries.Remove(index, out var old))
                {
                    _totalBytes -= old.ByteSize;
                    old.Release();
                }
                _entries[index] = image;
                _totalBytes += image.ByteSize;
                EvictIfNeeded();
                return image;
            }
        }
        finally
        {
            lock (_lock)
            {
                if (_inflight.TryGetValue(index, out var running) && running.Target == target)
                {
                    _inflight.Remove(index);
                }
            }
        }
    }

    private static bool Covers(PageSize running, PageSize wanted)
    {
        if (running.IsEmpty) return true;
        if (wanted.IsEmpty) return false;
        return running.Width >= wanted.Width * 0.9 && running.Height >= wanted.Height * 0.9;
    }

    private void EvictIfNeeded()
    {
        while (_totalBytes > LimitBytes && _entries.Count > 1)
        {
            var farthest = -1;
            var farthestDistance = -1;
            foreach (var key in _entries.Keys)
            {
                var d = Math.Abs(key - _position);
                if (d > farthestDistance)
                {
                    farthestDistance = d;
                    farthest = key;
                }
            }
            // 表示中の位置そのものは残す
            if (farthestDistance <= 0) break;
            var img = _entries[farthest];
            _entries.Remove(farthest);
            _totalBytes -= img.ByteSize;
            img.Release();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var img in _entries.Values)
            {
                img.Release();
            }
            _entries.Clear();
            _totalBytes = 0;
        }
    }
}
