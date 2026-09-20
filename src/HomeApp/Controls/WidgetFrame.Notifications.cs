using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace HomeApp.Controls;

public sealed partial class WidgetFrame
{
    private UIElement BuildNotifications()
    {
        StatusText.Text = "Windows通知";
        var panel = new Grid { RowSpacing = Spacing };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing };
        var refresh = new Button { Content = "更新" };
        var allow = new Button { Content = "通知へのアクセスを許可", Visibility = Visibility.Collapsed };
        actions.Children.Add(refresh);
        actions.Children.Add(allow);
        panel.Children.Add(actions);
        var body = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(body, 1);
        panel.Children.Add(body);

        // Each rendered view owns its polling and cancellation, including reloads.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        CancellationTokenSource? lifetime = null;
        var busy = false;
        string? previous = null;

        async Task ReadAsync(bool requestAccess = false)
        {
            if (busy || lifetime is null) return;
            var ct = lifetime.Token;
            busy = true;
            refresh.IsEnabled = allow.IsEnabled = false;
            try
            {
                try { _ = Package.Current.Id; }
                catch (InvalidOperationException)
                {
                    body.Content = Text("通知の読み取りにはアプリの登録が必要です。docs/windows-notifications.md の手順で登録して、HomeAppを起動し直してください。", 12, true);
                    StatusText.Text = "アプリ未登録";
                    timer.Stop();
                    return;
                }

                var listener = UserNotificationListener.Current;
                // Consent is requested only from an explicit button click on the UI thread.
                var access = requestAccess
                    ? await listener.RequestAccessAsync().AsTask(ct)
                    : listener.GetAccessStatus();
                if (ct.IsCancellationRequested) return;
                allow.Visibility = access == UserNotificationListenerAccessStatus.Unspecified
                    ? Visibility.Visible : Visibility.Collapsed;
                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    previous = null;
                    body.Content = Text(access == UserNotificationListenerAccessStatus.Denied
                        ? "通知へのアクセスが無効です。Windowsの設定 → プライバシーとセキュリティ → 通知でHomeAppを許可してから、更新してください。"
                        : "許可すると、Windowsの通知センターに残っている通知を表示します。通知はこのPC上で読み取り、外部へ送信しません。", 12, true);
                    StatusText.Text = access == UserNotificationListenerAccessStatus.Denied ? "アクセスが無効" : "アクセス許可が必要";
                    return;
                }

                var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(ct);
                if (ct.IsCancellationRequested) return;
                // Access can be revoked during an outstanding read; clear previously shown content.
                if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                {
                    previous = null;
                    body.Content = Text("通知へのアクセスが無効になりました。Windowsの設定を確認してください。", 12, true);
                    StatusText.Text = "アクセスが無効";
                    return;
                }
                var items = new List<(uint Id, string Title, string Body, string Caption)>();
                foreach (var notification in notifications.OrderByDescending(n => n.CreationTime).Take(50))
                {
                    try
                    {
                        var texts = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)
                            ?.GetTextElements().Select(t => t.Text).ToArray() ?? [];
                        var app = notification.AppInfo.DisplayInfo.DisplayName;
                        items.Add((notification.Id, texts.FirstOrDefault() ?? app,
                            string.Join("\n", texts.Skip(1)), $"{app} · {notification.CreationTime.ToLocalTime():M/d HH:mm}"));
                    }
                    catch (Exception) { /* A removed app or unsupported toast must not hide other notifications. */ }
                }
                var signature = System.Text.Json.JsonSerializer.Serialize(items.Select(i => new { i.Id, i.Title, i.Body, i.Caption }));
                if (signature != previous)
                {
                    var list = ConnectedList();
                    foreach (var item in items)
                        AddConnectedItem(list, item.Title, item.Body, item.Caption, false,
                            new(item.Title, item.Body, item.Caption));
                    body.Content = items.Count == 0 ? Text("通知センターに表示できる通知はありません。", 12, true) : list;
                    previous = signature;
                }
                StatusText.Text = $"Windows · 最新{items.Count}件 · {DateTime.Now:HH:mm}更新";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                previous = null;
                body.Content = Text($"通知を読み込めませんでした。アプリの登録とWindowsの通知アクセス設定を確認して、更新してください。（0x{ex.HResult:X8}）", 12, true);
                StatusText.Text = "読み込み失敗";
            }
            finally
            {
                busy = false;
                if (!ct.IsCancellationRequested) refresh.IsEnabled = allow.IsEnabled = true;
            }
        }

        refresh.Click += async (_, _) => await ReadAsync();
        allow.Click += async (_, _) => await ReadAsync(true);
        timer.Tick += async (_, _) => await ReadAsync();
        panel.Loaded += async (_, _) =>
        {
            lifetime?.Dispose();
            lifetime = new CancellationTokenSource();
            timer.Start();
            await ReadAsync();
        };
        panel.Unloaded += (_, _) =>
        {
            timer.Stop();
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
        };
        return panel;
    }
}
