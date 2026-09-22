# Google接続

HomeAppのメールとカレンダーは、同じGoogleアカウントから読み込みます。

- Gmail：受信トレイの最新20件。件名、送信者、受信日時、未読状態、本文プレビュー。
- Googleカレンダー：メインカレンダーの選択日の予定。月グリッドから選択した日の予定も下に表示します。終日・複数日・繰り返し予定に対応し、時刻はPCのタイムゾーンで表示します。
- 読み込みはウィジェット表示時、日付変更時、および「更新」ボタンで行います。
- 現段階では1つのGoogleアカウントです。メール全文、添付、メール検索、別カレンダーの選択、定期同期、送信・編集は未実装です。

## 初回設定

1. [Google Cloud Console](https://console.cloud.google.com/)で利用するプロジェクトを選ぶか作成します。
2. APIライブラリから **Gmail API** と **Google Calendar API** を有効にします。
3. Google Auth PlatformでOAuth同意画面を設定します。アプリ名は `HomeApp`、サポートと連絡先は自分のメールアドレスにします。個人利用なら対象を「外部」にし、「対象」→「テストユーザー」に利用するGoogleアカウントを追加します。「データアクセス」→「スコープを追加または削除」で `https://www.googleapis.com/auth/gmail.readonly` と `https://www.googleapis.com/auth/calendar.events.readonly` を追加して保存します。
4. 「クライアント」で種類が **デスクトップアプリ** のOAuthクライアントを作成し、JSONをダウンロードします。Webアプリやサービスアカウント用のJSONは使用しません。
5. HomeAppの「設定」→「Google接続設定」、または未接続ウィジェットの「Googleに接続」を開きます。
6. JSONファイルのフルパスを入力して「Googleに接続」を押します。ブラウザーでログインし、Gmailとカレンダーの両方の読み取りを許可します。
7. アプリに戻るとメールと予定を読み込みます。再接続時はJSONのパスを省略できます。

アプリ固有のOAuthクライアントは、このリポジトリには同梱していません。JSONはリポジトリ外に保管してください。配布用にする際は、GoogleのOAuth公開・審査要件を別途満たす必要があります。テスト用の同意画面ではGoogleのポリシーにより再認証が必要になる場合があります。

## 権限と保存

要求する権限は `gmail.readonly` と `calendar.events.readonly` です。送信、既読化、予定変更は行いません。標準ブラウザー、ループバック受信、PKCEを使うGoogle公式.NETライブラリで認証します。

トークンとOAuthクライアント設定は `%LOCALAPPDATA%\HomeApp\GoogleAuth` にWindowsのユーザー単位の暗号化で保存します。`workspace.json`やログには保存しません。メールと予定はメモリ内のみで扱い、HTMLは実行しません。

「Googleを切断」はこのPCのトークンを削除し、Googleへのトークン取り消しも試みます。ネットワークなどで取り消せなかった場合は、[Googleアカウントの接続管理](https://myaccount.google.com/connections)でHomeAppのアクセス権を解除してください。OAuthクライアント設定は再接続用に残ります。

## 接続できないとき

- 初回接続失敗：JSONがデスクトップアプリ用か、ファイルが存在するか、Googleのテストユーザーに登録されているかを確認します。
- 権限エラー：両APIを有効にし、両方の読み取り権限を許可して再接続します。
- 認証切れ：「Google接続設定」から再接続します。
- 読み込み失敗・タイムアウト：接続を確認し「更新」を押します。前の日付の結果が新しい日付を上書きすることはありません。
- 月表示で予定が見えない：カレンダーの下へスクロールするか、ウィジェットを広げます。

## 他の接続方式を追加する場合

`HomeApp.Core`の`IMailProvider`と`ICalendarProvider`が表示側との境界です。Googleの認証とREST形式は`HomeApp.Google`に閉じ込め、WinUIとWindows暗号化ストアは`HomeApp`に置いています。

将来のMicrosoft Graph、IMAP、CalDAVなどは別のプロバイダーとして実装し、`WidgetFrame.SetProviders`に渡せます。認証方法をCoreのデータ型へ混ぜないでください。複数アカウント対応時にはアカウントIDとプロバイダーIDによる選択・トークン分離を追加します。

## 参照

- [GoogleのデスクトップOAuth仕様](https://developers.google.com/identity/protocols/oauth2/native-app)
- [公式.NET認証ライブラリ](https://googleapis.dev/dotnet/Google.Apis.Auth/latest/api/Google.Apis.Auth.OAuth2.GoogleWebAuthorizationBroker.html)
- [Gmailメッセージ取得](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/get)
- [Calendar予定取得](https://developers.google.com/workspace/calendar/api/v3/reference/events/list)
