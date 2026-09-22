using System.Net;
using System.Text;
using HomeApp.Core;

namespace HomeApp.Core.Tests;

public sealed class RssReaderTests
{
    [Fact]
    public async Task Rss2_ReadsArticles_SortsDates_DeduplicatesAndStripsHtml()
    {
        var feed = await Read("""
            <rss version="2.0"><channel><title>News &amp; Updates</title>
              <item><guid>a</guid><title>Older</title><pubDate>Fri, 11 Sep 2026 08:00:00 GMT</pubDate><link>/older</link></item>
              <item><guid>b</guid><title>New</title><pubDate>Sat, 12 Sep 2026 08:00:00 GMT</pubDate>
                <link>/new</link><description><![CDATA[<p>Hello &amp; welcome</p><script>alert('no')</script><p>Next</p>]]></description></item>
              <item><guid>b</guid><title>Duplicate</title></item>
            </channel></rss>
            """);
        Assert.Equal("News & Updates", feed.Title);
        Assert.Equal(2, feed.Articles.Count);
        Assert.Equal("New", feed.Articles[0].Title);
        Assert.Equal("Hello & welcome Next", feed.Articles[0].Summary);
        Assert.Equal("https://example.com/new", feed.Articles[0].Link!.AbsoluteUri);
        Assert.Equal(TimeSpan.Zero, feed.Articles[0].PublishedAt!.Value.Offset);
    }

    [Fact]
    public async Task Atom_UsesAlternateLinks_XmlBase_Xhtml_AndUpdatedDate()
    {
        var feed = await Read("""
            <feed xmlns="http://www.w3.org/2005/Atom" xml:base="https://news.example.org/blog/">
              <title>Atom</title><entry xml:base="2026/">
                <id>entry-1</id><title>Story</title><updated>2026-09-12T12:00:00+09:00</updated>
                <link rel="self" href="entry.xml"/><link rel="alternate" href="story"/>
                <content type="xhtml"><div xmlns="http://www.w3.org/1999/xhtml"><p>One</p><p>Two</p></div></content>
              </entry>
            </feed>
            """);
        var item = Assert.Single(feed.Articles);
        Assert.Equal("https://news.example.org/blog/2026/story", item.Link!.AbsoluteUri);
        Assert.Equal("One Two", item.Summary);
        Assert.Equal(TimeSpan.FromHours(9), item.PublishedAt!.Value.Offset);
    }

    [Fact]
    public async Task Rss1_ReadsNamespacedItemsAndDates()
    {
        var feed = await Read("""
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/" xmlns:dc="http://purl.org/dc/elements/1.1/">
              <channel rdf:about="https://example.com/feed"><title>RDF feed</title></channel>
              <item rdf:about="https://example.com/story"><title>RDF story</title><link>https://example.com/story</link><dc:date>2026-09-12T09:00:00Z</dc:date></item>
            </rdf:RDF>
            """);
        Assert.Equal("RDF feed", feed.Title);
        Assert.NotNull(Assert.Single(feed.Articles).PublishedAt);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("https://user:password@example.com/story")]
    public async Task UnsafeArticleLinks_AreNotOfferedForOpening(string link)
    {
        var feed = await Read($"<rss><channel><item><title>Story</title><link>{link}</link></item></channel></rss>");
        Assert.Null(Assert.Single(feed.Articles).Link);
    }

    [Theory]
    [InlineData("file:///C:/feed.xml")]
    [InlineData("https://user:password@example.com/feed")]
    [InlineData("not a url")]
    public async Task InvalidFeedUrls_DoNotSendRequests(string url)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Must not send")));
        await Assert.ThrowsAsync<ConnectionException>(() => new RssReader(http).ReadAsync(url, default));
    }

    [Theory]
    [InlineData("<html><body>Not a feed</body></html>")]
    [InlineData("<rss>")]
    [InlineData("<!DOCTYPE rss [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]><rss><channel><title>&xxe;</title></channel></rss>")]
    public async Task InvalidDocumentsAndDtd_AreRejected(string xml) =>
        await Assert.ThrowsAsync<ConnectionException>(() => Read(xml));

    [Fact]
    public async Task EmptyFeed_IsValid_AndLargeFeedIsRejected()
    {
        Assert.Empty((await Read("<rss><channel><title>Empty</title></channel></rss>")).Articles);
        await Assert.ThrowsAsync<ConnectionException>(() => Read(new string('x', RssReader.MaxBytes + 1)));
    }

    [Fact]
    public async Task RedirectedFeed_ResolvesRelativeLinksAgainstFinalLocation()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, "https://new.example.org/blog/feed"),
            Content = new StringContent("<rss><channel><item><title>Story</title><link>article</link></item></channel></rss>")
        })));
        var feed = await new RssReader(http).ReadAsync("https://example.com/feed", default);
        Assert.Equal("https://new.example.org/blog/article", Assert.Single(feed.Articles).Link!.AbsoluteUri);
    }

    [Fact]
    public async Task RequestFailure_IsActionable_WithoutLeakingServerResponse()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        { Content = new StringContent("private server diagnostics") })));
        var error = await Assert.ThrowsAsync<ConnectionException>(() => new RssReader(http).ReadAsync("https://example.com/feed", default));
        Assert.Contains("URL", error.Message);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public async Task Cancellation_StopsResponseRead()
    {
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.OK);
        }));
        using var ct = new CancellationTokenSource();
        var request = new RssReader(http).ReadAsync("https://example.com/feed", ct.Token);
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task XmlEncodingDeclaration_IsRespected()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"iso-8859-1\"?><rss><channel><title>Café</title></channel></rss>";
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(Encoding.Latin1.GetBytes(xml)) })));
        Assert.Equal("Café", (await new RssReader(http).ReadAsync("https://example.com/feed", default)).Title);
    }

    [Fact]
    public void FeedSettings_SurviveCopyAndOldWorkspaceJson()
    {
        var widget = new WidgetState { Kind = WidgetKind.Rss, RssFeedUrl = "https://example.com/feed" };
        Assert.Equal(widget.RssFeedUrl, widget.Copy().RssFeedUrl);
        var old = System.Text.Json.JsonSerializer.Deserialize<WidgetState>("{}");
        Assert.Equal("", old!.RssFeedUrl);
    }

    private static async Task<FeedContent> Read(string xml)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") });
        }));
        return await new RssReader(http).ReadAsync("https://example.com/feed.xml", default);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
