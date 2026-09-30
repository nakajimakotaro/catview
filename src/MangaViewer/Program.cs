using System.Text;
using Avalonia;

namespace MangaViewer;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ZIP 内ファイル名の Shift_JIS デコードに必要（仕様 2 章）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
