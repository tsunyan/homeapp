# HomeApp

自分のホーム画面を組み立てる Windows デスクトップアプリ。時計・カレンダー・メモなどのウィジェットを、キャンバス上に自由に配置します。

まだ開発初期です。ウィジェットの中身はサンプルデータで、外部サービスへの接続は未実装です。設計は [docs/specification-draft.md](docs/specification-draft.md) にあります。

## 必要なもの

| | |
|---|---|
| OS | Windows 11（x64） |
| SDK | .NET 10 SDK — バージョンは [global.json](global.json) で `10.0.400` に固定 |

Windows App SDK は NuGet 経由で復元されるため、別途インストールは不要です。x64 専用で、他のアーキテクチャ向けの構成はありません。

## ビルド

```bash
dotnet restore src/HomeApp/HomeApp.csproj -r win-x64
```

```bash
dotnet build src/HomeApp/HomeApp.csproj -c Release -r win-x64 --no-restore
```

`HomeApp.Core` だけを確認したいときは、こちらが速いです（プラットフォーム非依存）。

```bash
dotnet build src/HomeApp.Core/HomeApp.Core.csproj -c Release
```

## 実行

```bash
dotnet run --project src/HomeApp/HomeApp.csproj -c Debug
```

## 配布用の発行

自己完結（`SelfContained` + `WindowsAppSDKSelfContained`）で、.NET ランタイムを含む約 227MB の一式が出力されます。

```bash
dotnet publish src/HomeApp/HomeApp.csproj -c Release -r win-x64
```

出力先: `src/HomeApp/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/HomeApp.exe`

## テスト

テストプロジェクトはまだありません。`tests/` 以下に `*.Tests.csproj` を置けば、CI が自動的に見つけて実行します（[.github/workflows/ci.yml](.github/workflows/ci.yml) の Test ステップ）。ワークフロー側の変更は不要です。

## 構成

```
src/
  HomeApp.Core/      プラットフォーム非依存。モデル・検証・保存・レイアウト計算
    Workspace.cs       WorkspaceState / BoardState / WidgetState と LayoutMath
    WorkspaceStore.cs  一時ファイル経由の原子的な保存と、破損時のバックアップ復元
    SampleData.cs      ウィジェットの種類とサンプルデータ
  HomeApp/           WinUI 3 のUI層
    DashboardPage     ボード表示、編集モード、インスペクタ、ズーム
    Controls/WidgetFrame  ウィジェット1枚。ドラッグ・リサイズと種類ごとの描画
```

`HomeApp.Core` は WinUI に依存しません。レイアウト計算や保存形式の検証はこちら側にあります。

保存先は `%LOCALAPPDATA%\HomeApp\workspace.json` です。読み込みに失敗した場合、元のファイルは `workspace.recovery-<日時>.json` として保全してから、バックアップの復元を試みます。

## CI

`main` はブランチ保護下にあり、直接 push できません。変更は PR 経由で、以下がすべて green である必要があります。

| ジョブ | ランナー | 内容 |
|---|---|---|
| `build` | windows-latest | 復元・ビルド・テスト。WinUI 3 のため Windows ランナー必須 |
| `secrets` | ubuntu-latest | gitleaks による全履歴の走査 |
| `analyze` | windows-latest | CodeQL（C#）。実ビルドを伴うため `build-mode: manual` |

GitHub Actions は改ざん耐性のためタグではなくコミット SHA で固定しています。

## 既知の制限

- `MainWindow` は空の `MainPage` を表示します。`DashboardPage` は実装済みですが、まだ画面遷移が繋がっていません
- ウィジェットのデータはすべてサンプルで、メール・カレンダー・RSS の実接続はありません

## ライセンス

MIT License — [LICENSE](LICENSE) を参照してください。
