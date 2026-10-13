using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;

namespace Deutchezz.Application.Bookings;

/// <summary>
/// The single source of truth for "a cancelled booking is immutable" (SoT 7n / BR-042). Every
/// operation that would mutate a booking's stay or rooms calls <see cref="EnsureMutable"/> first,
/// so the client always receives the same clear 409 <c>booking.cancelled</c> instead of a
/// per-endpoint state message.
/// </summary>
public static class BookingGuards
{
    public const string CancelledCode = "booking.cancelled";

    /// <summary>Returns a 409 conflict error when <paramref name="status"/> is Cancelled; otherwise null.</summary>
    public static Error? EnsureMutable(BookingStatus status) =>
        status == BookingStatus.Cancelled
            ? Error.Conflict(CancelledCode, "This booking is cancelled and can no longer be modified.")
            : null;
}
