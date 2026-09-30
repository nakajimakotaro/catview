namespace MangaViewer.Core.Layout;

/// <summary>
/// 表示単位のリストを計算する（仕様 5 章）。UI に依存しない純粋な関数。
/// 途中の横長ページで以降のペアリングが変わるため、必ず先頭から順に組み立てる。
/// </summary>
public static class SpreadLayout
{
    /// <param name="pageCount">ページ数</param>
    /// <param name="isLandscape">各ページの横長判定。横長ページの自動単独表示が OFF の場合はすべて false を渡す</param>
    /// <param name="viewMode">表示モード</param>
    /// <param name="coverSingle">表紙単独表示</param>
    /// <param name="shift">1ページずらし</param>
    public static IReadOnlyList<DisplayUnit> Compute(
        int pageCount,
        IReadOnlyList<bool> isLandscape,
        ViewMode viewMode,
        bool coverSingle,
        bool shift)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        if (isLandscape.Count < pageCount)
        {
            throw new ArgumentException("横長判定の数がページ数より少ない", nameof(isLandscape));
        }

        var units = new List<DisplayUnit>(pageCount);
        if (viewMode == ViewMode.Single)
        {
            for (var p = 0; p < pageCount; p++)
            {
                units.Add(new DisplayUnit(p, 1));
            }
            return units;
        }

        var i = 0;
        if (coverSingle && i < pageCount)
        {
            units.Add(new DisplayUnit(0, 1));
            i = 1;
        }
        if (shift && i < pageCount)
        {
            units.Add(new DisplayUnit(i, 1));
            i++;
        }
        while (i < pageCount)
        {
            if (isLandscape[i] || i + 1 >= pageCount || isLandscape[i + 1])
            {
                units.Add(new DisplayUnit(i, 1));
                i++;
            }
            else
            {
                units.Add(new DisplayUnit(i, 2));
                i += 2;
            }
        }
        return units;
    }

    /// <summary>指定ページを含む表示単位のインデックスを返す。見つからなければ末尾側に丸める。</summary>
    public static int FindUnitIndex(IReadOnlyList<DisplayUnit> units, int page)
    {
        if (units.Count == 0)
        {
            return -1;
        }
        int lo = 0, hi = units.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var u = units[mid];
            if (page < u.Start)
            {
                hi = mid - 1;
            }
            else if (page > u.End)
            {
                lo = mid + 1;
            }
            else
            {
                return mid;
            }
        }
        return Math.Clamp(lo, 0, units.Count - 1);
    }
}
