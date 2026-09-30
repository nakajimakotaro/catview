namespace MangaViewer.Core;

/// <summary>
/// 自然順ソート（仕様 3.4）。数字部分は数値として比較し、それ以外は大文字小文字を区別せずに比較する。
/// </summary>
public sealed class NaturalSortComparer : IComparer<string>
{
    public static readonly NaturalSortComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var cx = x[i];
            var cy = y[j];
            if (char.IsAsciiDigit(cx) && char.IsAsciiDigit(cy))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var result = CompareDigits(x.AsSpan(si, i - si), y.AsSpan(sj, j - sj));
                if (result != 0) return result;
            }
            else
            {
                var result = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (result != 0) return result;
                i++;
                j++;
            }
        }

        var remaining = (x.Length - i).CompareTo(y.Length - j);
        // 同値扱いになった場合も順序を安定させる
        return remaining != 0 ? remaining : string.CompareOrdinal(x, y);
    }

    private static int CompareDigits(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        // 桁数に上限がないよう、先頭の 0 を除いた桁数 → 辞書順で比較する
        var ta = a.TrimStart('0');
        var tb = b.TrimStart('0');
        if (ta.Length != tb.Length) return ta.Length.CompareTo(tb.Length);
        var c = ta.SequenceCompareTo(tb);
        if (c != 0) return c;
        // 数値が等しい場合は 0 埋めの少ない方を先にする（"1" < "01"）
        return a.Length.CompareTo(b.Length);
    }
}
