namespace MangaViewer.Core.Sources;

/// <summary>ファイルを開けなかったことを示す。Message はそのままユーザーに表示できる文言。</summary>
public class PageSourceException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class EncryptedFileException() : PageSourceException("暗号化されたファイルは開けません");
