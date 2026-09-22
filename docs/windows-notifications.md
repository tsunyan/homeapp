# Windows通知ウィジェット

Windowsの通知センターに残っているトースト通知を、アプリ名・日時とともに最新50件表示します。15秒ごとに更新し、クリックで本文を表示できます。通知の削除・返信・送信元アプリの起動は行いません。通知内容は保存せず、外部にも送信しません。ウィジェットを外すと定期更新を停止します。

## ローカル開発での登録

通知リスナーにはパッケージIDと `userNotificationListener` capability が必要です。通常の未登録exeだけでは利用できません。ローカル開発用の登録スクリプトを用意しています。

1. HomeAppをビルドして終了します。
2. PowerShellで実行します（パスはビルド先に合わせてください）。

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Register-NotificationIdentity.ps1 -AppDirectory ./src/HomeApp/bin/Release/net10.0-windows10.0.26100.0/win-x64
   ```

3. 登録に開発者モードが必要と表示された場合は、Windowsの設定で開発者モードを有効にして再実行します。スクリプトはWindowsのセキュリティ設定を変更しません。
4. 登録したフォルダーのHomeApp.exeを起動し、Windows通知ウィジェットの「通知へのアクセスを許可」を押して、Windowsの確認画面で許可します。

拒否した場合はWindowsの設定 → プライバシーとセキュリティ → 通知からHomeAppのアクセスを有効にし、ウィジェットを更新してください。登録したフォルダー内の `NotificationIdentity` は削除しないでください。ビルド先を変更した場合は再登録します。同一IDの別パッケージがインストール済みの場合は、その配布方法に合わせて更新してください。

これはローカル開発用の登録です。配布時は `Package.appxmanifest` の通知capabilityを含む署名済みMSIX、または署名済み外部ロケーションパッケージを使用します。

配布版では信頼された署名とインストーラーによる登録を用意し、利用者の開発者モードや手動コマンドは不要にします。通知へのアクセス許可は利用者ごとに必要です。

保存先は登録前後ともユーザーのLocalAppData内の `HomeApp` に固定し、パッケージによるファイル書き込みの仮想化も無効にしています。別のパッケージの子プロセスとして起動した旧開発版では、そのパッケージ内へ保存が転送されていた場合があります。その場合は元データを保持したまま移行してください。登録スクリプトの再実行時は、既存IDのバージョンを増やして更新します。

参照: [Microsoft: Notification listener](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener)、[パッケージIDの付与](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)
