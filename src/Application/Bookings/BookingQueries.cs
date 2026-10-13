using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;

namespace Deutchezz.Application.Bookings;

/// <summary>
/// Reusable booking query predicates so the dashboard count and the bookings list can never drift.
/// </summary>
public static class BookingQueries
{
    /// <summary>
    /// The single "overdue check-out" predicate (v1.12): a guest who is still checked in past the
    /// scheduled check-out moment. Used by both the admin dashboard count and the reception/admin
    /// list filter (<c>overdueCheckOuts=true</c>) so the number on the card always matches the list
    /// it links to.
    /// </summary>
    public static IQueryable<Booking> OverdueCheckOuts(IQueryable<Booking> query, DateTime nowUtc) =>
        query.Where(b => b.Status == BookingStatus.CheckedIn && b.CheckOutAt < nowUtc);
}
