# MangaViewer

PDF・ZIP（.zip / .cbz）・画像フォルダに対応したデスクトップ向けマンガビューア。仕様は [manga-viewer-spec.md](manga-viewer-spec.md) を参照。

## 構成

| プロジェクト | 内容 |
|---|---|
| `src/MangaViewer.Core` | UI 非依存の層。ページソース（PDF / ZIP / フォルダ）、見開きレイアウト計算、キャッシュ、設定・閲覧記録 |
| `src/MangaViewer` | Avalonia UI（MVVM）。描画、入力処理、ショートカット |
| `tests/MangaViewer.Core.Tests` | Core の単体テスト（レイアウト規則、自然順ソート、ZIP の文字コード、ヘッダ読み取り、PDF、記録、キャッシュ） |

主要パッケージ：Avalonia 12.1.3、PDFtoImage 5.2.1、SkiaSharp 3.119.4。
PDFtoImage 5.3 以降は SkiaSharp 4 系を要求し Avalonia 12 と食い違うため、5.2.1 に固定している（仕様 11.1）。

## ビルドと実行

.NET 10 SDK が必要。

```sh
dotnet build MangaViewer.sln
dotnet run --project src/MangaViewer -- <ファイルまたはフォルダのパス>
dotnet test
```

配布用ビルドの例：

```sh
dotnet publish src/MangaViewer -c Release -r osx-arm64 --self-contained   # win-x64 / linux-x64 なども可
```

## 仕様の解釈・補足

- **Shift+←/→（1 ページだけ進む / 戻る）**：レイアウトは先頭から組み立てる規則（5 章）を保つ必要があるため、1ページずらしを切り替えて表示位置が 1 ページずれる表示単位へ移動する。切り替えても 1 ページずれない場合（横長ページの直前など）は通常のページ移動になる。
- **クリック領域**：画面を 3 分割し、左右 1/3 がページ送り、中央のダブルクリックがフルスクリーン切り替え（左右領域でのダブルクリックは連続ページ送りとして扱う）。
- **ホイール**：表示がウィンドウからはみ出している場合（ズーム中・幅合わせなど）はスクロールし、端に達したらページ送り。
- **次 / 前のファイル**：同じフォルダ内の .zip / .cbz / .pdf と、画像を含むサブフォルダを自然順で並べて前後を開く。
- **画像単体を開いた場合**：記録のキーはその画像を含むフォルダのパス。表示位置は開いた画像のページが優先される。
- **macOS**：Ctrl のショートカットは Command でも動作する。
- **PDF の「原寸」**：ページサイズ（ポイント）を 96dpi 換算したサイズを原寸とする。
- **設定項目**：先読みページ数・キャッシュ上限・背景色は「設定」メニューのプリセットから選ぶ。`settings.json` を直接編集すれば任意の値（背景色は `#RRGGBB`）も指定できる。

## ファイルの関連付け

- **macOS**：`.app` バンドルを作成し、[packaging/macos/Info.plist](packaging/macos/Info.plist) を `Contents/Info.plist` として配置する。アイコン [packaging/macos/AppIcon.icns](packaging/macos/AppIcon.icns) は `Contents/Resources/` に配置する。Finder から開いたファイルはアプリ側で受け取る。
- **Linux**：[packaging/linux/mangaviewer.desktop](packaging/linux/mangaviewer.desktop) を `~/.local/share/applications/` に配置する。アイコン [packaging/linux/mangaviewer.png](packaging/linux/mangaviewer.png) は `~/.local/share/icons/hicolor/256x256/apps/` に配置する。
- **Windows**：インストーラ（MSIX / Inno Setup など）で `.zip` / `.cbz` / `.pdf` の関連付けを登録し、`MangaViewer.exe "%1"` で起動させる。

## 保存場所

| OS | ディレクトリ |
|---|---|
| Windows | `%AppData%\MangaViewer\` |
| macOS | `~/Library/Application Support/MangaViewer/` |
| Linux | `~/.config/MangaViewer/`（`XDG_CONFIG_HOME` を優先） |

`settings.json`（全体設定）と `history.db`（ファイルごとの閲覧記録、上限 1,000 件）を保存する。
