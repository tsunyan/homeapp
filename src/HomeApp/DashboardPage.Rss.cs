using HomeApp.Controls;
using HomeApp.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HomeApp;

public sealed partial class DashboardPage
{
    private async Task EditRssAsync(WidgetFrame frame)
    {
        var url = new TextBox
        {
            Header = "RSS・AtomのURL", Text = frame.Model.RssFeedUrl,
            PlaceholderText = "https://example.com/feed.xml", MaxLength = 4096,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var explanation = new TextBlock
        {
            Text = "配信元ごとにウィジェットを追加できます。URLを空にして保存すると登録を解除します。",
            TextWrapping = TextWrapping.Wrap, FontSize = 12
        };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
        var panel = new StackPanel { Spacing = 12, Width = 360 };
        panel.Children.Add(url);
        panel.Children.Add(explanation);
        panel.Children.Add(error);
        var dialog = NewDialog("RSSの配信元", panel, "保存");
        dialog.PrimaryButtonClick += (sender, args) =>
        {
            if (!string.IsNullOrWhiteSpace(url.Text) && !RssReader.TryGetWebUri(url.Text, out _))
            {
                error.Text = "http://またはhttps://で始まる、ユーザー名・パスワードを含まないURLを入力してください。";
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (frame.Model.RssFeedUrl == url.Text.Trim()) return;
        PushUndo();
        frame.Model.RssFeedUrl = url.Text.Trim();
        frame.RefreshContent();
        await SaveAsync();
    }
}
