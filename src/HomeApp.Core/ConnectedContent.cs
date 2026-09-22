namespace HomeApp.Core;

// Providers own authentication and wire formats; widgets consume these read-only models.
public interface IMailProvider
{
    string DisplayName { get; }
    Task<IReadOnlyList<MailItem>> ReadInboxAsync(int limit, CancellationToken cancellationToken);
}

public interface ICalendarProvider
{
    string DisplayName { get; }
    Task<IReadOnlyList<CalendarItem>> ReadEventsAsync(DateTimeOffset from, DateTimeOffset until,
        CancellationToken cancellationToken);
}

public sealed record MailItem(string Id, string Subject, string Sender, string Preview,
    DateTimeOffset ReceivedAt, bool IsUnread);

// All-day dates are floating calendar dates; EndDate is exclusive, as in iCalendar.
public sealed record CalendarItem(string Id, string Title, string Description, string Location,
    DateTimeOffset? Start, DateTimeOffset? End, DateOnly? StartDate, DateOnly? EndDate)
{
    public bool IsAllDay => StartDate.HasValue;
}

public sealed record ContentDetail(string Title, string Text, string Caption, Uri? Link = null);

public sealed class ConnectionException(string message) : Exception(message);
