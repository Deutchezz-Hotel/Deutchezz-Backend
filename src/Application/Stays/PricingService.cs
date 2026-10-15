using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Stays;

public record PricingQuoteRoom(Guid RoomTypeId, int Quantity);

/// <summary>
/// A price quote for a prospective stay (v1.14). <c>BookingType</c> is "online" (default) or
/// "walkIn"; walk-in quotes relax the "check-in in the past" rule to the slot grid.
/// </summary>
public record PricingQuoteRequest(
    DateOnly CheckInDate,
    string? CheckInTime,
    DateOnly CheckOutDate,
    string? CheckOutTime,
    string? BookingType,
    List<PricingQuoteRoom> Rooms);

public record PricingQuoteLine(
    Guid RoomTypeId,
    string RoomTypeName,
    int Quantity,
    decimal RatePerNight,
    int Nights,
    int OverageMinutes,
    string? TierLabel,
    decimal Subtotal,
    decimal ExtraCharge,
    decimal LineTotal);

public record PricingQuote(
    int Nights,
    int OverageMinutes,
    List<PricingQuoteLine> Lines,
    decimal Subtotal,
    decimal ExtraChargeAmount,
    decimal Total);

public interface IPricingService
{
    /// <summary>Prices a prospective stay end to end (the single source of the billing formula).</summary>
    Task<Result<PricingQuote>> QuoteAsync(PricingQuoteRequest request, CancellationToken cancellationToken);

    /// <summary>Loads the surcharge tiers for the given room types, keyed by room type id.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<SurchargeTier>>> GetTiersAsync(
        IEnumerable<Guid> roomTypeIds, CancellationToken cancellationToken);
}

public class PricingService(IAppDbContext db, IStayPolicyService stays) : IPricingService
{
    public async Task<Result<PricingQuote>> QuoteAsync(PricingQuoteRequest request, CancellationToken cancellationToken)
    {
        if (request.Rooms is null || request.Rooms.Count == 0)
            return Error.Validation("quote.no_rooms", "At least one room type is required");
        if (request.Rooms.Any(r => r.Quantity < 1))
            return Error.Validation("quote.invalid_quantity", "Room quantity must be at least 1");
        if (request.Rooms.Select(r => r.RoomTypeId).Distinct().Count() != request.Rooms.Count)
            return Error.Validation("quote.duplicate_roomtype", "Room types must be unique within a quote");

        var isWalkIn = string.Equals(request.BookingType, "walkIn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.BookingType, "reception", StringComparison.OrdinalIgnoreCase);

        var stay = await stays.ResolveAsync(
            request.CheckInDate, request.CheckInTime, request.CheckOutDate, request.CheckOutTime,
            isWalkIn, cancellationToken);
        if (stay.IsFailure)
            return stay.Error;
        var s = stay.Value;

        if (s.Nights > s.Policy.MaxStayNights)
            return Error.Validation("booking.too_long", $"Stay cannot exceed {s.Policy.MaxStayNights} nights");

        var ids = request.Rooms.Select(r => r.RoomTypeId).Distinct().ToList();
        var types = await db.RoomTypes
            .Where(rt => ids.Contains(rt.Id))
            .ToDictionaryAsync(rt => rt.Id, cancellationToken);
        if (types.Count != ids.Count)
            return Error.Validation("quote.roomtype_invalid", "One or more room types are invalid");

        var tiers = await GetTiersAsync(ids, cancellationToken);

        var lines = new List<PricingQuoteLine>(request.Rooms.Count);
        var subtotalTotal = 0m;
        var extraTotal = 0m;
        foreach (var room in request.Rooms)
        {
            var type = types[room.RoomTypeId];
            var priced = StayPricing.ComputeLine(
                type.BasePrice, room.Quantity, s.Nights, s.Duration, tiers[room.RoomTypeId], s.Policy.GraceHours);
            var subtotal = type.BasePrice * room.Quantity * priced.Nights;
            subtotalTotal += subtotal;
            extraTotal += priced.OverageAmount;
            lines.Add(new PricingQuoteLine(
                type.Id,
                type.Name,
                room.Quantity,
                type.BasePrice,
                priced.Nights,
                priced.OverageMinutes,
                priced.TierLabel,
                subtotal,
                priced.OverageAmount,
                priced.LineTotal));
        }

        return new PricingQuote(
            s.Nights, s.OverageMinutes, lines, subtotalTotal, extraTotal, subtotalTotal + extraTotal);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<SurchargeTier>>> GetTiersAsync(
        IEnumerable<Guid> roomTypeIds, CancellationToken cancellationToken)
    {
        var ids = roomTypeIds.Distinct().ToList();
        var rows = await db.RoomTypeSurchargeTiers
            .Where(t => ids.Contains(t.RoomTypeId))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, IReadOnlyList<SurchargeTier>>();
        foreach (var id in ids)
            result[id] = [];

        foreach (var group in rows.GroupBy(r => r.RoomTypeId))
        {
            result[group.Key] = group
                .OrderBy(t => t.FromHours)
                .Select(t => new SurchargeTier(t.FromHours, t.ToHours, t.Amount))
                .ToList();
        }

        return result;
    }
}
