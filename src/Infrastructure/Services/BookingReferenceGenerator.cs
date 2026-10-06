using System.Text;
using Deutchezz.Application.Abstractions;

namespace Deutchezz.Infrastructure.Services;

public class BookingReferenceGenerator : IBookingReferenceGenerator
{
    public string Generate()
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        var random = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        return $"DHI-{date}-{random}";
    }
}