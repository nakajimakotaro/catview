namespace MangaViewer.Core.Persistence;

/// <summary>ファイルごとの閲覧記録（仕様 7.3）。</summary>
public sealed record HistoryRecord(
    string Path,
    int LastPage,
    int PageCount,
    ViewMode ViewMode,
    ReadingDirection Direction,
    bool Shift,
    bool CoverSingle,
    DateTimeOffset LastOpenedAt);
