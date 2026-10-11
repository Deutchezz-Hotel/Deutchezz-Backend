namespace Deutchezz.Application.Abstractions;

/// <summary>
/// Business time for the hotel, in the Africa/Lagos time zone. All timestamps are
/// stored in UTC; this clock converts them to the business time zone.
/// </summary>
public interface IBusinessClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
    DateTimeOffset ConvertFromUtc(DateTimeOffset utc);
    DateOnly ToLocalDate(DateTimeOffset utc);
}