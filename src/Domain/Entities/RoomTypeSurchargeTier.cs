using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

/// <summary>
/// A fixed-amount overstay surcharge band for a room type (v1.14). Bands are half-open on the
/// lower edge: when the part of a stay not covered by whole 24-hour blocks ("remainder") falls in
/// <c>FromHours &lt; remainderHours ≤ ToHours</c>, this <see cref="Amount"/> is charged per room.
/// A remainder within the grace window is free; a remainder beyond the last band is charged one
/// additional full nightly rate (the "full night" rule).
/// </summary>
public class RoomTypeSurchargeTier : BaseEntity
{
    public Guid RoomTypeId { get; set; }

    /// <summary>Exclusive lower bound, in hours, of the band.</summary>
    public int FromHours { get; set; }

    /// <summary>Inclusive upper bound, in hours, of the band.</summary>
    public int ToHours { get; set; }

    /// <summary>Fixed amount (NGN) charged per room when the remainder falls in this band.</summary>
    public decimal Amount { get; set; }
}
