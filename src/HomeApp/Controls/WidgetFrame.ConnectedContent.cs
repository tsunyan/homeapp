using HomeApp.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HomeApp.Controls;

public sealed partial class WidgetFrame
{
    private IMailProvider? _mailProvider;
    private ICalendarProvider? _calendarProvider;
    private CancellationTokenSource? _contentRequest;
    public event EventHandler? ConnectionRequested;
    public event EventHandler<ContentDetail>? ConnectedDetailRequested;

    public void SetProviders(IMailProvider? mail, ICalendarProvider? calendar)
    {
        _mailProvider = mail;
        _calendarProvider = calendar;
        if (Model.Kind is WidgetKind.Mail or WidgetKind.Calendar) RefreshContent();
    }

    public void CancelContentRequest()
    {
        _contentRequest?.Cancel();
        _contentRequest?.Dispose();
        _contentRequest = null;
    }

    private UIElement ConnectionPrompt()
    {
        StatusText.Text = "未接続";
        var panel = new StackPanel { Spacing = Spacing };
        panel.Children.Add(Text("Googleを接続すると、メールと予定を読み込めます。", 12, true));
        var connect = new Button { Content = "Googleに接続" };
        connect.Click += (_, _) => ConnectionRequested?.Invoke(this, EventArgs.Empty);
        panel.Children.Add(connect);
        return panel;
    }

    private UIElement BuildMail()
    {
        if (_mailProvider is null) return ConnectionPrompt();
        var panel = new Grid { RowSpacing = Spacing };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var refresh = new Button { Content = "受信トレイを更新" };
        panel.Children.Add(refresh);
        var body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(body, 1);
        panel.Children.Add(body);
        refresh.Click += async (_, _) => await LoadMailAsync(body);
        body.Loaded += async (_, _) => await LoadMailAsync(body);
        return panel;
    }

    private async Task LoadMailAsync(ContentControl target)
    {
        var provider = _mailProvider;
        if (provider is null) return;
        CancelContentRequest();
        _contentRequest = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = _contentRequest.Token;
        target.Content = Text("メールを読み込み中…", 12, true);
        StatusText.Text = provider.DisplayName + " · 読み込み中";
        try
        {
            var items = await provider.ReadInboxAsync(20, ct);
            if (ct.IsCancellationRequested) return;
            var list = ConnectedList();
            foreach (var item in items)
            {
                var caption = $"{item.Sender} · {item.ReceivedAt.ToLocalTime():M/d HH:mm}";
                AddConnectedItem(list, item.Subject, item.Preview, caption, item.IsUnread,
                    new(item.Subject, item.Preview, caption + "\nGmail · 本文のプレビュー（全文・添付ファイルは未取得）"));
            }
            target.Content = items.Count == 0 ? Text("受信トレイは空です。", 12, true) : list;
            StatusText.Text = $"{provider.DisplayName} · 最新{items.Count}件 · {DateTime.Now:HH:mm}更新";
        }
        catch (Exception ex) { ShowLoadError(target, ex, ct); }
    }

    private async Task LoadCalendarAsync(ContentControl target, DateTimeOffset date)
    {
        var provider = _calendarProvider;
        if (provider is null) { target.Content = ConnectionPrompt(); return; }
        CancelContentRequest();
        _contentRequest = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = _contentRequest.Token;
        target.Content = Text("予定を読み込み中…", 12, true);
        StatusText.Text = $"{date:M月d日} · 読み込み中";
        try
        {
            // Construct each local midnight separately, preserving DST day lengths.
            var from = new DateTimeOffset(DateTime.SpecifyKind(date.Date, DateTimeKind.Local));
            var until = new DateTimeOffset(DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Local));
            var items = await provider.ReadEventsAsync(from, until, ct);
            if (ct.IsCancellationRequested) return;
            var list = ConnectedList();
            foreach (var item in items)
            {
                var time = item.IsAllDay ? $"終日 · {item.StartDate:M/d} – {item.EndDate?.AddDays(-1):M/d}"
                    : $"{item.Start?.ToLocalTime():M/d HH:mm} – {item.End?.ToLocalTime():M/d HH:mm}";
                AddConnectedItem(list, item.Title, item.Location, time, true,
                    new(item.Title, item.Description, time + "\n" + item.Location));
            }
            target.Content = items.Count == 0 ? Text($"{date:M月d日}の予定はありません。", 12, true) : list;
            StatusText.Text = $"{provider.DisplayName} · {date:M月d日} · {items.Count}件";
        }
        catch (Exception ex) { ShowLoadError(target, ex, ct); }
    }

    private void ShowLoadError(ContentControl target, Exception error, CancellationToken ct)
    {
        // A replaced/unloaded view must never overwrite the current view's status.
        if (_contentRequest is null || _contentRequest.Token != ct) return;
        var text = error switch
        {
            ConnectionException => error.Message,
            OperationCanceledException => "読み込みがタイムアウトしました。再度更新してください。",
            _ => "読み込めませんでした。接続を確認して更新してください。"
        };
        target.Content = Text(text, 12, true);
        StatusText.Text = "読み込み失敗";
    }

    private ListView ConnectedList()
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
        list.ItemClick += (_, args) =>
        {
            // Explicit ListViewItem containers report their Content as ClickedItem.
            if (args.ClickedItem is FrameworkElement { Tag: ContentDetail detail })
                ConnectedDetailRequested?.Invoke(this, detail);
        };
        return list;
    }

    private void AddConnectedItem(ListView list, string title, string preview, string caption, bool emphasized, ContentDetail detail)
    {
        var panel = new StackPanel { Spacing = 4, Padding = new(0, Spacing, 0, Spacing), Tag = detail };
        var heading = Text(title);
        heading.FontWeight = emphasized ? FontWeights.SemiBold : FontWeights.Normal;
        heading.MaxLines = 2;
        heading.TextTrimming = TextTrimming.CharacterEllipsis;
        panel.Children.Add(heading);
        var summary = Text(preview, 12, true);
        summary.MaxLines = 2;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        panel.Children.Add(summary);
        panel.Children.Add(Text(caption, 11, true));
        var row = new ListViewItem { Content = panel, Tag = detail, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(row, title + " " + caption);
        list.Items.Add(row);
    }
}
