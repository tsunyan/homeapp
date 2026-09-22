using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HomeApp.Core;

namespace HomeApp.Google;

public sealed class GoogleContentProvider(HttpClient http, Func<CancellationToken, Task<string>> accessToken)
    : IMailProvider, ICalendarProvider
{
    string IMailProvider.DisplayName => "Gmail";
    string ICalendarProvider.DisplayName => "Googleカレンダー";

    private async Task<JsonDocument> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken(ct));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new ConnectionException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Googleの認証が切れています。接続設定から再接続してください。",
                HttpStatusCode.Forbidden => "読み取り権限またはAPIの有効化を確認してください。接続設定から再接続できます。",
                HttpStatusCode.TooManyRequests => "Googleへのアクセスが集中しています。少し待ってから更新してください。",
                _ => "Googleから読み込めませんでした。時間をおいて更新してください。"
            });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<IReadOnlyList<MailItem>> ReadInboxAsync(int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 50);
        const string root = "https://gmail.googleapis.com/gmail/v1/users/me/messages";
        using var page = await GetAsync($"{root}?labelIds=INBOX&maxResults={limit}", cancellationToken);
        if (!page.RootElement.TryGetProperty("messages", out var messages)) return [];
        var result = new List<MailItem>();
        // Bounded inbox preview; metadata avoids downloading bodies and attachments.
        foreach (var message in messages.EnumerateArray().Take(limit))
        {
            if (message.ValueKind != JsonValueKind.Object) continue;
            var id = String(message, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            using var data = await GetAsync($"{root}/{Uri.EscapeDataString(id)}?format=metadata&metadataHeaders=Subject&metadataHeaders=From", cancellationToken);
            result.Add(ParseMail(data.RootElement));
        }
        return result.OrderByDescending(m => m.ReceivedAt).ToArray();
    }

    public async Task<IReadOnlyList<CalendarItem>> ReadEventsAsync(DateTimeOffset from, DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        if (until <= from) throw new ArgumentException("The end must follow the start.", nameof(until));
        var url = "https://www.googleapis.com/calendar/v3/calendars/primary/events"
            + $"?singleEvents=true&orderBy=startTime&showDeleted=false&maxResults=250"
            + $"&timeMin={Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))}"
            + $"&timeMax={Uri.EscapeDataString(until.ToString("O", CultureInfo.InvariantCulture))}";
        var result = new List<CalendarItem>();
        string? token = null;
        var seen = new HashSet<string>();
        do
        {
            using var page = await GetAsync(url + (token is null ? "" : "&pageToken=" + Uri.EscapeDataString(token)), cancellationToken);
            if (page.RootElement.TryGetProperty("items", out var items))
                foreach (var item in items.EnumerateArray())
                    if (String(item, "status") != "cancelled") result.Add(ParseEvent(item));
            token = String(page.RootElement, "nextPageToken");
            if (token.Length == 0) break;
            if (!seen.Add(token)) throw new ConnectionException("予定のページを読み込めませんでした。再度更新してください。");
        } while (true);
        return result;
    }

    internal static MailItem ParseMail(JsonElement item)
    {
        var headers = item.TryGetProperty("payload", out var payload) && payload.TryGetProperty("headers", out var values)
            ? values.EnumerateArray().ToArray() : [];
        string Header(string name)
        {
            foreach (var header in headers)
                if (string.Equals(String(header, "name"), name, StringComparison.OrdinalIgnoreCase))
                    return String(header, "value");
            return "";
        }
        var subject = Header("Subject");
        var received = long.TryParse(String(item, "internalDate"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var epoch)
            && epoch >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds()
            && epoch <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch) : DateTimeOffset.MinValue;
        return new(String(item, "id"), subject.Length == 0 ? "（件名なし）" : subject,
            Header("From"), WebUtility.HtmlDecode(String(item, "snippet")),
            received,
            item.TryGetProperty("labelIds", out var labels) && labels.EnumerateArray().Any(l => l.GetString() == "UNREAD"));
    }

    internal static CalendarItem ParseEvent(JsonElement item)
    {
        var start = item.GetProperty("start");
        var end = item.GetProperty("end");
        DateTimeOffset? Time(JsonElement value) => value.TryGetProperty("dateTime", out var t)
            ? DateTimeOffset.Parse(t.GetString()!, CultureInfo.InvariantCulture) : null;
        DateOnly? Date(JsonElement value) => value.TryGetProperty("date", out var d)
            ? DateOnly.ParseExact(d.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        var title = String(item, "summary");
        return new(String(item, "id"), title.Length == 0 ? "（タイトルなし）" : title,
            WebUtility.HtmlDecode(String(item, "description")), String(item, "location"),
            Time(start), Time(end), Date(start), Date(end));
    }

    private static string String(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
