using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MangaViewer.Core.Persistence;

/// <summary>閲覧記録の SQLite ストア（仕様 7 章）。</summary>
public sealed class HistoryStore : IDisposable
{
    public const int DefaultMaxRecords = 1000;

    // Windows ではパスの大文字小文字を区別しない（仕様 7.2）
    private const string PathCollation = "MV_PATH";

    private readonly SqliteConnection _connection;

    public HistoryStore(string file)
    {
        if (file != ":memory:")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        }
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString());
        _connection.CreateCollation(PathCollation, (a, b) => string.Compare(a, b,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        _connection.Open();
        Execute($"""
            CREATE TABLE IF NOT EXISTS history (
                path TEXT PRIMARY KEY COLLATE {PathCollation},
                last_page INTEGER NOT NULL,
                page_count INTEGER NOT NULL,
                view_mode INTEGER NOT NULL,
                direction INTEGER NOT NULL,
                shift INTEGER NOT NULL,
                cover_single INTEGER NOT NULL,
                last_opened_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS history_last_opened_at ON history(last_opened_at);
            """);
    }

    public HistoryRecord? Get(string path)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT path, last_page, page_count, view_mode, direction, shift, cover_single, last_opened_at
            FROM history WHERE path = $path
            """;
        cmd.Parameters.AddWithValue("$path", AppPaths.NormalizePath(path));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new HistoryRecord(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            (ViewMode)reader.GetInt32(3),
            (ReadingDirection)reader.GetInt32(4),
            reader.GetInt32(5) != 0,
            reader.GetInt32(6) != 0,
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public void Save(HistoryRecord record)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO history (path, last_page, page_count, view_mode, direction, shift, cover_single, last_opened_at)
            VALUES ($path, $last_page, $page_count, $view_mode, $direction, $shift, $cover_single, $last_opened_at)
            ON CONFLICT(path) DO UPDATE SET
                last_page = excluded.last_page,
                page_count = excluded.page_count,
                view_mode = excluded.view_mode,
                direction = excluded.direction,
                shift = excluded.shift,
                cover_single = excluded.cover_single,
                last_opened_at = excluded.last_opened_at
            """;
        cmd.Parameters.AddWithValue("$path", AppPaths.NormalizePath(record.Path));
        cmd.Parameters.AddWithValue("$last_page", record.LastPage);
        cmd.Parameters.AddWithValue("$page_count", record.PageCount);
        cmd.Parameters.AddWithValue("$view_mode", (int)record.ViewMode);
        cmd.Parameters.AddWithValue("$direction", (int)record.Direction);
        cmd.Parameters.AddWithValue("$shift", record.Shift ? 1 : 0);
        cmd.Parameters.AddWithValue("$cover_single", record.CoverSingle ? 1 : 0);
        cmd.Parameters.AddWithValue("$last_opened_at", record.LastOpenedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public int Count()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM history";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 上限を超えた分を最終閲覧日時の古い順に削除する（仕様 7.8）。
    /// ファイルの存在確認による削除は行わない。
    /// </summary>
    public int Prune(int maxRecords = DefaultMaxRecords)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM history WHERE path IN (
                SELECT path FROM history ORDER BY last_opened_at DESC LIMIT -1 OFFSET $max
            )
            """;
        cmd.Parameters.AddWithValue("$max", maxRecords);
        return cmd.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
