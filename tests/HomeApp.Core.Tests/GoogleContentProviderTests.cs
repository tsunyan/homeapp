using System.Net;
using System.Text;
using HomeApp.Core;
using HomeApp.Google;

namespace HomeApp.Core.Tests;

public sealed class GoogleContentProviderTests
{
    [Fact]
    public async Task Inbox_UsesReadOnlyMetadata_MapsUnreadAndDecodedPreview()
    {
        var urls = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            urls.Add(request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(Json(urls.Count == 1 ? """
                {"messages":[{"id":"one"},{"id":"two"}]}
                """ : urls.Count == 2 ? """
                {"id":"one","snippet":"Hello &amp; goodbye","internalDate":"1000","labelIds":["INBOX","UNREAD"],
                 "payload":{"headers":[{"name":"subject","value":"First"},{"name":"FROM","value":"Sender"}]}}
                """ : """
                {"id":"two","snippet":"Second","internalDate":"2000","labelIds":["INBOX"],"payload":{"headers":[]}}
                """));
        }));
        var result = await Provider(http).ReadInboxAsync(20, default);
        Assert.Contains("labelIds=INBOX", urls[0]);
        Assert.Contains("maxResults=20", urls[0]);
        Assert.All(urls.Skip(1), url => Assert.Contains("format=metadata", url));
        Assert.Equal("two", result[0].Id);
        Assert.Equal("（件名なし）", result[0].Subject);
        Assert.False(result[0].IsUnread);
        Assert.True(result[1].IsUnread);
        Assert.Equal("Sender", result[1].Sender);
        Assert.Equal("Hello & goodbye", result[1].Preview);
    }

    [Fact]
    public async Task Inbox_EmptyResultDoesNotFetchMessages()
    {
        var count = 0;
        using var http = new HttpClient(new Handler((_, _) => { count++; return Task.FromResult(Json("{}")); }));
        Assert.Empty(await Provider(http).ReadInboxAsync(20, default));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Inbox_SkipsInvalidMessageIds_AndFetchesValidEntries()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(paths.Count == 1 ? """
                {"messages":[{},null,42,[],{"id":null},{"id":42},{"id":false},
                  {"id":{}},{"id":[]},{"id":""},{"id":" "},{"id":"valid"}]}
                """ : """
                {"id":"valid","internalDate":"1000"}
                """));
        }));

        var result = await Provider(http).ReadInboxAsync(20, default);

        Assert.Equal("valid", Assert.Single(result).Id);
        Assert.Equal(new[] { "/gmail/v1/users/me/messages", "/gmail/v1/users/me/messages/valid" }, paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("1000")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"\"")]
    [InlineData("\"invalid\"")]
    [InlineData("\"9223372036854775808\"")]
    [InlineData("\"9223372036854775807\"")]
    [InlineData("\"-9223372036854775808\"")]
    [InlineData("\"253402300800000\"")]
    [InlineData("\"-62135596800001\"")]
    public async Task Inbox_InvalidDateFallsBack_AndKeepsOtherMessages(string? dateJson)
    {
        var calls = 0;
        var dateProperty = dateJson is null ? "" : ",\"internalDate\":" + dateJson;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(++calls switch
        {
            1 => """{"messages":[{"id":"invalid-date"},{"id":"valid"}]}""",
            2 => "{\"id\":\"invalid-date\"" + dateProperty + "}",
            _ => """{"id":"valid","internalDate":"1000"}"""
        }))));

        var result = await Provider(http).ReadInboxAsync(20, default);

        Assert.Equal(3, calls);
        Assert.Equal(new[] { "valid", "invalid-date" }, result.Select(item => item.Id));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1000), result[0].ReceivedAt);
        Assert.Equal(DateTimeOffset.MinValue, result[1].ReceivedAt);
    }

    [Theory]
    [InlineData("-62135596800000", -62135596800000L)]
    [InlineData("0", 0L)]
    [InlineData("253402300799999", 253402300799999L)]
    public async Task Inbox_PreservesSupportedDateRange(string date, long expectedMilliseconds)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(++calls == 1
            ? """{"messages":[{"id":"valid"}]}"""
            : "{\"id\":\"valid\",\"internalDate\":\"" + date + "\"}"))));

        var item = Assert.Single(await Provider(http).ReadInboxAsync(20, default));

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(expectedMilliseconds), item.ReceivedAt);
    }

    [Fact]
    public async Task Calendar_FollowsPages_ExpandsRecurrences_PreservesAllDayAndOffsets()
    {
        var urls = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(Json(urls.Count == 1 ? """
                {"nextPageToken":"next/+","items":[
                  {"id":"holiday","summary":"Holiday","start":{"date":"2026-09-09"},"end":{"date":"2026-09-11"}},
                  {"id":"deleted","status":"cancelled"}]}
                """ : """
                {"items":[{"id":"instance","recurringEventId":"series","summary":"Meeting","location":"Room A",
                "start":{"dateTime":"2026-09-09T23:30:00-07:00"},"end":{"dateTime":"2026-09-10T00:30:00-07:00"}}]}
                """));
        }));
        var from = DateTimeOffset.Parse("2026-09-09T00:00:00+09:00");
        var result = await Provider(http).ReadEventsAsync(from, from.AddDays(1), default);
        Assert.Equal(2, result.Count);
        Assert.Contains("/calendars/primary/events", urls[0]);
        Assert.Contains("singleEvents=true", urls[0]);
        Assert.Contains("orderBy=startTime", urls[0]);
        Assert.Contains("pageToken=next%2F%2B", urls[1]);
        Assert.True(result[0].IsAllDay);
        Assert.Equal(new DateOnly(2026, 9, 11), result[0].EndDate);
        Assert.Null(result[0].Start);
        Assert.False(result[1].IsAllDay);
        Assert.Equal(TimeSpan.FromHours(-7), result[1].Start!.Value.Offset);
        Assert.Equal("Room A", result[1].Location);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "認証")]
    [InlineData(HttpStatusCode.Forbidden, "権限")]
    [InlineData(HttpStatusCode.TooManyRequests, "集中")]
    [InlineData(HttpStatusCode.InternalServerError, "読み込めません")]
    public async Task Failures_AreActionable_AndDoNotExposeResponseData(HttpStatusCode code, string expected)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(code)
        { Content = new StringContent("private-account-data") })));
        var error = await Assert.ThrowsAsync<ConnectionException>(() => Provider(http).ReadInboxAsync(20, default));
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("private-account-data", error.Message);
    }

    [Fact]
    public async Task Cancellation_StopsInFlightRead()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        }));
        using var cancel = new CancellationTokenSource();
        var read = Provider(http).ReadInboxAsync(20, cancel.Token);
        await started.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task Calendar_RejectsRepeatedPageToken()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("""{"nextPageToken":"same","items":[]} """))));
        var now = DateTimeOffset.Now;
        await Assert.ThrowsAsync<ConnectionException>(() => Provider(http).ReadEventsAsync(now, now.AddDays(1), default));
    }

    private static GoogleContentProvider Provider(HttpClient http) => new(http, _ => Task.FromResult("test-token"));
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
