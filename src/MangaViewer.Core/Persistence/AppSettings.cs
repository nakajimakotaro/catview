using System.Text.Json;
using System.Text.Json.Serialization;

namespace MangaViewer.Core.Persistence;

/// <summary>全体設定（仕様 10 章）。</summary>
public sealed class AppSettings
{
    public ViewMode DefaultViewMode { get; set; } = ViewMode.Spread;

    public ReadingDirection DefaultDirection { get; set; } = ReadingDirection.RightToLeft;

    public bool DefaultCoverSingle { get; set; } = true;

    public bool AutoSingleLandscape { get; set; } = true;

    public FitMode FitMode { get; set; } = FitMode.Window;

    /// <summary>背景色（#RRGGBB）。</summary>
    public string BackgroundColor { get; set; } = "#000000";

    public InterpolationQuality Interpolation { get; set; } = InterpolationQuality.High;

    public int PrefetchCount { get; set; } = 4;

    public int CacheLimitMB { get; set; } = 512;

    public WindowPlacement? Window { get; set; }

    public static AppSettings Load(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                var settings = JsonSerializer.Deserialize(File.ReadAllText(file), AppSettingsJsonContext.Default.AppSettings);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // 壊れた設定ファイルはデフォルトで上書きする
        }
        return new AppSettings();
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings));
        File.Move(temp, file, overwrite: true);
    }

    private void Normalize()
    {
        PrefetchCount = Math.Clamp(PrefetchCount, 0, 32);
        CacheLimitMB = Math.Clamp(CacheLimitMB, 64, 16384);
        if (string.IsNullOrWhiteSpace(BackgroundColor)) BackgroundColor = "#000000";
    }
}

public sealed class WindowPlacement
{
    public int X { get; set; }

    public int Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext;
