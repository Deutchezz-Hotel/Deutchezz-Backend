using Deutchezz.Infrastructure.Services;

namespace Deutchezz.UnitTests.Infrastructure;

public class BookingReferenceGeneratorTests
{
    private static readonly char[] Forbidden = ['0', 'O', '1', 'I', 'L'];

    [Fact]
    public void Reference_uses_DHI_year_and_6_character_suffix()
    {
        var generator = new BookingReferenceGenerator(new TestBusinessClock(new DateOnly(2026, 10, 10)));

        var reference = generator.Generate();

        Assert.StartsWith("DHI-2026-", reference);
        Assert.Equal(15, reference.Length); // DHI-YYYY-XXXXXX (11 chars)? DHI- is 3 + year 4 + dash 1 + 6 = 14? "DHI-2026-XXXXXX" = 3+1+4+1+6 = 15. Actually 3+4+1+6 = 14? "DHI-" (4) + "2026" (4) + "-" (1) + 6 = 15? 4+4=8, +1=9, +6=15. Correct.
        Assert.All(reference.Substring(reference.Length - 6), c => Assert.DoesNotContain(c, Forbidden));
    }

    [Fact]
    public void References_are_not_unique_per_call_but_are_unambiguous_alphabet()
    {
        var generator = new BookingReferenceGenerator(new TestBusinessClock(new DateOnly(2026, 1, 1)));
        var references = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var reference = generator.Generate();
            Assert.Matches(@"^DHI-2026-[A-HJ-NP-Z2-9]{6}$", reference);
            references.Add(reference);
        }

        Assert.True(references.Count > 100, "references should vary across calls");
    }

    private sealed class TestBusinessClock(DateOnly today) : Deutchezz.Application.Abstractions.IBusinessClock
    {
        public DateTimeOffset UtcNow => new(today.Year, today.Month, today.Day, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => today;
        public DateTimeOffset ConvertFromUtc(DateTimeOffset utc) => utc;
        public DateOnly ToLocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(utc.UtcDateTime);
    }
}