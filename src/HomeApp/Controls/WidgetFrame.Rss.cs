using HomeApp.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HomeApp.Controls;

public sealed partial class WidgetFrame
{
    private static readonly RssReader Feeds = new(new HttpClient(new HttpClientHandler
    {
        UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All
    }) { Timeout = TimeSpan.FromSeconds(25) });
    public event EventHandler? RssSettingsRequested;

    private UIElement BuildRss()
    {
        var panel = new Grid { RowSpacing = Spacing };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing };
        var configured = !string.IsNullOrWhiteSpace(Model.RssFeedUrl);
        var settings = new Button { Content = configured ? "配信元を変更" : "配信元を登録" };
        settings.Click += (_, _) => RssSettingsRequested?.Invoke(this, EventArgs.Empty);
        var refresh = new Button { Content = "更新", IsEnabled = configured };
        AutomationProperties.SetName(refresh, "RSSの記事を更新");
        actions.Children.Add(settings);
        actions.Children.Add(refresh);
        panel.Children.Add(actions);
        var body = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(body, 1);
        panel.Children.Add(body);
        if (!configured)
        {
            body.Content = Text("RSS・AtomのURLを登録すると、新しい記事をここで確認できます。", 12, true);
            StatusText.Text = "配信元が未登録です";
        }
        else
        {
            refresh.Click += async (_, _) => await LoadRssAsync(body);
            body.Loaded += async (_, _) => await LoadRssAsync(body);
        }
        return panel;
    }

    private async Task LoadRssAsync(ContentControl target)
    {
        CancelContentRequest();
        _contentRequest = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = _contentRequest.Token;
        target.Content = Text("記事を読み込み中…", 12, true);
        StatusText.Text = "RSS · 読み込み中";
        try
        {
            var feed = await Feeds.ReadAsync(Model.RssFeedUrl, ct);
            if (ct.IsCancellationRequested) return;
            var list = ConnectedList();
            foreach (var article in feed.Articles)
            {
                var caption = feed.Title + (article.PublishedAt is { } date ? $" · {date.ToLocalTime():M/d HH:mm}" : "");
                AddConnectedItem(list, article.Title, article.Summary, caption, false,
                    new(article.Title, article.Summary, caption, article.Link));
            }
            target.Content = feed.Articles.Count == 0 ? Text("記事はまだありません。", 12, true) : list;
            StatusText.Text = $"RSS · {feed.Articles.Count}件 · {DateTime.Now:HH:mm}更新";
        }
        catch (Exception ex) { ShowLoadError(target, ex, ct); }
    }
}
