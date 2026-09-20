# HomeApp

自分のホーム画面を組み立てる Windows デスクトップアプリ。時計・カレンダー・メモなどのウィジェットを、キャンバス上に自由に配置します。

まだ開発初期です。GmailとGoogleカレンダーの読み取りに対応しています。初回の認証設定は [Google接続ガイド](docs/google-connection.md)、設計は [docs/specification-draft.md](docs/specification-draft.md) にあります。

## 必要なもの

| | |
|---|---|
| OS | Windows 11（x64） |
| SDK | .NET 10 SDK — [global.json](global.json) が下限として `10.0.400` を指定 |

[global.json](global.json) の `rollForward` は `latestFeature` なので、これは固定ではなく下限です。より新しいフィーチャーバンド（`10.0.5xx` など）が入っていればそちらが使われます。厳密に揃える必要が出たときは `rollForward` を `disable` か `patch` に変更してください。

Windows App SDK は NuGet 経由で復元されるため、別途インストールは不要です。x64 専用で、他のアーキテクチャ向けの構成はありません。

## ビルド

```bash
dotnet build src/HomeApp/HomeApp.csproj -c Release -r win-x64
```

これ1つで復元も走ります。CI は復元を別ステップに分けているので、同じ手順を手元で再現したい場合のみ次の2つを順に実行してください（`--no-restore` は事前の復元が前提です）。

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

## 基本操作

- 初回は操作確認用の2つのボードと、7種類のウィジェットが表示されます。未接続のウィジェットには接続や登録の案内を表示し、メモとカスタムJSONには編集可能な入力例を用意しています。空のボードでは中央の「ウィジェットを追加」から始められます。
- 上部の「追加」で時計、カレンダー、メール、Windows通知、RSS、カスタムJSON、メモを追加します。
- RSS／ニュースは「配信元を登録」で公開RSS・AtomのURLを入力します。最新30件を表示し、記事を選ぶと概要と元記事へのリンクを開けます。「更新」で再取得、「配信元を変更」でURL変更・空欄にして登録解除ができます。設定はウィジェットごとに保存されます。
- 「配置を編集」をオンにすると、カードの見出しをドラッグして移動し、右下のハンドルでサイズを変更できます。右側の数値入力でも位置とサイズを調整できます。
- グリッド吸着は任意です。「整列」は現在の表示幅に合わせて明示的に並べ直します。ウィンドウサイズやモニターを変えただけでは保存配置を変更しません。
- 35〜200%のズーム、100%へのリセット、全体表示を利用できます。変更は`%LOCALAPPDATA%\HomeApp\workspace.json`へ保存されます。

## 配布用の発行

自己完結（`SelfContained` + `WindowsAppSDKSelfContained`）で、.NET ランタイムを含む約 227MB の一式が出力されます。

```bash
dotnet publish src/HomeApp/HomeApp.csproj -c Release -r win-x64
```

出力先: `src/HomeApp/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/HomeApp.exe`

## テスト

レイアウト計算、ワークスペースの保存・バックアップ復元、カスタムJSON、Google接続の復元・APIの読み取り、RSS・Atomの解析・エラー処理を `HomeApp.Core.Tests` で確認します。Google APIとRSSのテストは疑似HTTP応答を使い、実アカウントや配信元にアクセスしません。

```bash
dotnet test tests/HomeApp.Core.Tests/HomeApp.Core.Tests.csproj -c Release
```

## 構成

```
src/
  HomeApp.Core/      プラットフォーム非依存。モデル・検証・保存・レイアウト計算
    Workspace.cs       WorkspaceState / BoardState / WidgetState と LayoutMath
    WorkspaceStore.cs  一時ファイル経由の原子的な保存と、破損時のバックアップ復元
    SampleData.cs      ウィジェットの種類とサンプルデータ
    ConnectedContent.cs メール・カレンダープロバイダーの共通インターフェース
  HomeApp.Google/    Google OAuthとGmail・Calendar API。表示側から独立
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

- Google接続は1アカウントの受信トレイ最新20件のプレビューとメインカレンダーに対応。メール全文・添付・編集・定期同期は未実装です
- RSSはRSS 1.0／2.0とAtomに対応し、表示時と手動更新時に取得します。認証が必要なフィード、自動巡回、既読管理は未実装です
- Windows通知は通知センターの最新50件を15秒ごとに読み取り、クリックで詳細を表示します。利用にはアプリ登録とWindowsのアクセス許可が必要です。[登録手順](docs/windows-notifications.md)を参照してください。ローカル登録後の実通知取得を確認済みです
- 配布パッケージ、配布用OAuth審査、常駐動作は今後の技術検証対象です

## ライセンス

MIT License — [LICENSE](LICENSE) を参照してください。
