using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

public class BookingRoomType : BaseEntity
{
    public Guid BookingId { get; set; }
    public Guid RoomTypeId { get; set; }
    public int Quantity { get; set; }
    public decimal RatePerNight { get; set; }
    public int NumberOfNights { get; set; }
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Late-check-out surcharge for this line (v1.14), part of the booking's total. When the
    /// stay's overage falls in the half-day band this is <c>HalfDayPercent</c> of
    /// <see cref="RatePerNight"/> × <see cref="Quantity"/>; past the full-night band it is
    /// <c>FullNightPercent</c> of the same. Zero within the grace window.
    /// </summary>
    public decimal ExtraCharge { get; set; }
}