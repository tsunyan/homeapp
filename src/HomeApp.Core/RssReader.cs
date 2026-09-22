using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace HomeApp.Core;

public sealed record FeedArticle(string Title, string Summary, Uri? Link, DateTimeOffset? PublishedAt);
public sealed record FeedContent(string Title, IReadOnlyList<FeedArticle> Articles);

public sealed class RssReader(HttpClient http)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Rss1 = "http://purl.org/rss/1.0/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";

    public static bool TryGetWebUri(string? text, out Uri uri)
    {
        if (Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https" && parsed.UserInfo.Length == 0 && parsed.Host.Length > 0)
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    public async Task<FeedContent> ReadAsync(string url, CancellationToken ct)
    {
        if (!TryGetWebUri(url, out var uri)) throw new ConnectionException("RSS・AtomのURLをhttp://またはhttps://で指定してください。");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml");
        request.Headers.UserAgent.ParseAdd("HomeApp/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new ConnectionException(response.StatusCode switch
            {
                HttpStatusCode.NotFound => "フィードが見つかりません。URLを確認してください。",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "このフィードにはアクセスできません。公開フィードのURLを指定してください。",
                HttpStatusCode.TooManyRequests => "配信元のアクセス制限中です。時間をおいて更新してください。",
                _ => "配信元から読み込めませんでした。時間をおいて更新してください。"
            });
        if (response.Content.Headers.ContentLength > MaxBytes) throw TooLarge();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > MaxBytes) throw TooLarge();
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        buffer.Position = 0;
        ct.ThrowIfCancellationRequested();
        try
        {
            using var reader = XmlReader.Create(buffer, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes,
                IgnoreComments = true
            });
            var document = XDocument.Load(reader);
            return Parse(document, response.RequestMessage?.RequestUri ?? uri);
        }
        catch (XmlException) { throw new ConnectionException("RSS・Atomとして読み込めませんでした。WebページではなくフィードのURLを指定してください。"); }
    }

    private static ConnectionException TooLarge() => new("フィードが大きすぎます（上限2MB）。別のフィードを指定してください。");

    private static FeedContent Parse(XDocument document, Uri source)
    {
        var root = document.Root;
        XElement channel;
        IEnumerable<XElement> entries;
        bool atom;
        if (root?.Name == Atom + "feed")
        {
            channel = root;
            entries = root.Elements(Atom + "entry");
            atom = true;
        }
        else if (root?.Name == "rss" && root.Element("channel") is { } rss)
        {
            channel = rss;
            entries = rss.Elements("item");
            atom = false;
        }
        else if (root?.Name == XName.Get("RDF", "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
            && root.Element(Rss1 + "channel") is { } rdf)
        {
            channel = rdf;
            entries = root.Elements(Rss1 + "item");
            atom = false;
        }
        else throw new ConnectionException("対応形式はRSS 1.0・RSS 2.0・Atomです。フィードのURLを確認してください。");

        var ns = channel.Name.Namespace;
        var feedTitle = PlainText(channel.Element(ns + "title")?.Value, 160);
        var result = new List<FeedArticle>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.Take(1000))
        {
            var title = PlainText(entry.Element(ns + "title")?.Value, 300);
            var summary = atom ? entry.Element(Atom + "summary") ?? entry.Element(Atom + "content")
                : entry.Element(ns + "description") ?? entry.Element(Content + "encoded");
            // XHTML Atom content must retain tag boundaries before conversion to plain text.
            var summaryText = summary?.HasElements == true
                ? string.Concat(summary.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))) : summary?.Value;
            var linkNode = atom ? entry.Elements(Atom + "link").FirstOrDefault(l =>
                ((string?)l.Attribute("rel") is null or "alternate") && l.Attribute("href") is not null)
                : entry.Element(ns + "link");
            var linkText = atom ? (string?)linkNode?.Attribute("href") : linkNode?.Value;
            var link = ResolveLink(linkNode ?? entry, linkText, source);
            var id = atom ? entry.Element(Atom + "id")?.Value : entry.Element(ns + "guid")?.Value;
            var dateText = atom ? entry.Element(Atom + "published")?.Value ?? entry.Element(Atom + "updated")?.Value
                : entry.Element(ns + "pubDate")?.Value ?? entry.Element(Dc + "date")?.Value;
            DateTimeOffset? date = DateTimeOffset.TryParse(dateText?.Replace(" GMT", " +00:00"), CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
            var preview = PlainText(summaryText, 2000);
            if (title.Length == 0 && preview.Length == 0 && link is null) continue;
            var identity = !string.IsNullOrWhiteSpace(id) ? id : link?.AbsoluteUri;
            if (identity is not null && !identities.Add(identity)) continue;
            result.Add(new(title.Length == 0 ? "（タイトルなし）" : title, preview, link, date));
        }
        return new(feedTitle.Length == 0 ? source.Host : feedTitle,
            result.OrderByDescending(a => a.PublishedAt).Take(30).ToArray());
    }

    private static Uri? ResolveLink(XElement element, string? value, Uri source)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var basis = source;
        foreach (var ancestor in element.AncestorsAndSelf().Reverse())
        {
            var xmlBase = (string?)ancestor.Attribute(XNamespace.Xml + "base");
            if (xmlBase is not null && Uri.TryCreate(basis, xmlBase, out var resolvedBase)) basis = resolvedBase;
        }
        return Uri.TryCreate(basis, value.Trim(), out var link) && TryGetWebUri(link.AbsoluteUri, out var safe) ? safe : null;
    }

    private static string PlainText(string? html, int limit)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var timeout = TimeSpan.FromMilliseconds(200);
        try
        {
            var text = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline, timeout);
            text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.None, timeout);
            text = Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ", RegexOptions.None, timeout).Trim();
            return text.Length > limit ? text[..limit] + "…" : text;
        }
        catch (RegexMatchTimeoutException) { return "（プレビューを表示できません）"; }
    }
}
