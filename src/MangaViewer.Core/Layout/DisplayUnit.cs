namespace MangaViewer.Core.Layout;

/// <summary>一度に表示するページの組。Start から Count 枚の連続したページ。</summary>
public readonly record struct DisplayUnit(int Start, int Count)
{
    public int End => Start + Count - 1;

    public bool Contains(int page) => page >= Start && page <= End;

    public IEnumerable<int> Pages => Enumerable.Range(Start, Count);

    public override string ToString() => Count == 1 ? $"({Start})" : $"({Start},{End})";
}
