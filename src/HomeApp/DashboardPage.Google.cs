using HomeApp.Core;
using HomeApp.Google;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HomeApp;

public sealed partial class DashboardPage
{
    private readonly GoogleConnection _google;
    private Task? _googleRestoreTask;
    private bool _connectionDialogOpen;

    private Task RestoreGoogleAsync() => _googleRestoreTask ??= RestoreGoogleCoreAsync();

    private async Task RestoreGoogleCoreAsync()
    {
        try
        {
            await _google.RestoreAsync(CancellationToken.None);
            ApplyGoogleProviders();
        }
        catch { Notify("保存したGoogle接続を読み込めませんでした。接続設定から再接続してください。", InfoBarSeverity.Warning); }
    }

    private void ApplyGoogleProviders()
    {
        foreach (var frame in _frames.Values) frame.SetProviders(_google.Provider, _google.Provider);
    }

    private async Task ShowGoogleConnectionAsync()
    {
        if (_connectionDialogOpen) return;
        _connectionDialogOpen = true;
        using var lifetime = new CancellationTokenSource();
        try
        {
            await RestoreGoogleAsync();
            var path = new TextBox
            {
                Header = "OAuthクライアントJSONのパス（初回のみ）",
                PlaceholderText = "C:\\…\\client_secret_….json",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var status = new TextBlock
            {
                Text = _google.IsConnected ? "Googleに接続済み" : "Googleは未接続です。",
                TextWrapping = TextWrapping.Wrap
            };
            var connect = new Button { Content = _google.IsConnected ? "Googleに再接続" : "Googleに接続" };
            var disconnect = new Button { Content = "Googleを切断", IsEnabled = _google.IsConnected };
            var cancel = new Button { Content = "接続をキャンセル", IsEnabled = false };
            var panel = new StackPanel { Spacing = 12, Width = 380 };
            panel.Children.Add(new TextBlock
            {
                Text = "Gmailの受信トレイとメインカレンダーを読み取ります。Google Cloudで両方のAPIを有効にし、デスクトップアプリ用のOAuthクライアントJSONを指定してください。ログインはブラウザーで行います。",
                TextWrapping = TextWrapping.Wrap
            });
            var help = new HyperlinkButton
            {
                Content = "Google Cloudでクライアントを設定",
                NavigateUri = new Uri("https://console.cloud.google.com/auth/clients")
            };
            panel.Children.Add(help);
            panel.Children.Add(path);
            panel.Children.Add(status);
            panel.Children.Add(connect);
            panel.Children.Add(disconnect);
            panel.Children.Add(cancel);
            CancellationTokenSource? operation = null;
            Task pending = Task.CompletedTask;
            void SetBusy(bool busy)
            {
                connect.IsEnabled = !busy;
                path.IsEnabled = !busy;
                disconnect.IsEnabled = !busy && _google.IsConnected;
                cancel.IsEnabled = busy;
            }
            async Task RunAsync(bool remove)
            {
                operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                operation.CancelAfter(TimeSpan.FromMinutes(3));
                SetBusy(true);
                // Cancel outstanding reads before replacing or removing their credential.
                foreach (var frame in _frames.Values) frame.CancelContentRequest();
                status.Text = remove ? "切断中…" : "ブラウザーでGoogleにログインしてください…";
                try
                {
                    if (remove) await _google.DisconnectAsync(operation.Token);
                    else await _google.ConnectAsync(path.Text, operation.Token);
                    status.Text = remove ? "Googleを切断しました。" : "接続しました。Gmailとカレンダーを読み込めます。";
                }
                catch (OperationCanceledException) { status.Text = "接続操作をキャンセルしました。"; }
                catch (ConnectionException ex) { status.Text = ex.Message; }
                catch
                {
                    status.Text = remove
                        ? "切断処理でエラーが発生しました。Googleアカウント設定でもアクセス権を解除してください。"
                        : "接続できませんでした。JSONのパス、Googleのテストユーザー設定、ネットワークを確認してください。";
                }
                finally
                {
                    operation.Dispose();
                    operation = null;
                    SetBusy(false);
                    ApplyGoogleProviders();
                }
            }
            connect.Click += (_, _) => { pending = RunAsync(false); };
            disconnect.Click += (_, _) => { pending = RunAsync(true); };
            cancel.Click += (_, _) => operation?.Cancel();
            await NewDialog("Google接続設定", new ScrollViewer { Content = panel, MaxHeight = 520 }, null).ShowAsync();
            lifetime.Cancel();
            await pending;
        }
        finally { _connectionDialogOpen = false; }
    }

    private async Task ShowConnectedDetailAsync(ContentDetail detail)
    {
        var panel = new StackPanel { Spacing = 12, Width = 380 };
        panel.Children.Add(new TextBlock { Text = detail.Caption, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7 });
        panel.Children.Add(new TextBlock { Text = detail.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        if (detail.Link is { } link && RssReader.TryGetWebUri(link.AbsoluteUri, out _))
            panel.Children.Add(new HyperlinkButton { Content = "記事をブラウザーで開く", NavigateUri = link });
        await NewDialog(detail.Title, new ScrollViewer { Content = panel, MaxHeight = 450 }, null).ShowAsync();
    }
}
