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
    /// Overstay surcharge for this line (v1.14), part of the booking's total. The stay is billed as
    /// <see cref="NumberOfNights"/> whole 24-hour blocks (captured in <see cref="Subtotal"/>) plus
    /// the remainder, which is free inside the grace window, charged a fixed tier amount, or charged
    /// one additional full nightly rate. The applied tier is snapshotted in <see cref="SurchargeTier"/>.
    /// </summary>
    public decimal ExtraCharge { get; set; }

    /// <summary>
    /// Snapshot of the fixed-amount band applied to this line (v1.14), e.g. "5-10h" or "full-night";
    /// null when the remainder fell inside the grace window. Kept so historical lines never change
    /// when the tier table is later edited.
    /// </summary>
    public string? SurchargeTier { get; set; }
}