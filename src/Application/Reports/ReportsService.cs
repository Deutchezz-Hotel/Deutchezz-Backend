using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Reports;

public record DashboardResponse(
    int TotalRooms,
    int AvailableRooms,
    int OccupiedRooms,
    int MaintenanceRooms,
    int InactiveRooms,
    int TodayBookings,
    int TodayCheckIns,
    int TodayCheckOuts,
    int ActiveBookings,
    decimal RevenueToday,
    int OccupancyPercentage,
    int AvailableNow,
    int InHouse,
    int OverdueCheckOuts,
    int RoomsFreeingToday,
    IReadOnlyList<BookingStatusCount> BookingsByStatus,
    IReadOnlyList<DailyRevenue> RevenueTrend);

public record ReportFilterRequest(DateOnly? From, DateOnly? To);

public record DailyMetric(DateOnly Date, int Bookings, decimal Revenue);

public record DailyRevenue(DateOnly Date, decimal Revenue);

public record BookingStatusCount(string Status, int Count);

public record ReportResponse(
    DateOnly From,
    DateOnly To,
    int BookingsCount,
    int NightsBooked,
    int RoomsBooked,
    decimal Revenue,
    decimal Outstanding,
    int Cancelled,
    int RoomNightsSold,
    int RoomNightsCapacity,
    decimal OccupancyRate,
    List<DailyMetric> Daily);

public interface IReportsService
{
    Task<Result<DashboardResponse>> GetDashboardAsync(CancellationToken cancellationToken);
    Task<Result<ReportResponse>> GetReportAsync(ReportFilterRequest request, CancellationToken cancellationToken);
}

public class ReportsService(IAppDbContext db, IBusinessClock clock) : IReportsService
{
    public async Task<Result<DashboardResponse>> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var nowUtc = clock.UtcNow.UtcDateTime;
        var lagosOffset = clock.ConvertFromUtc(clock.UtcNow).Offset;

        var rooms = await db.Rooms
            .Where(r => !r.IsDeleted)
            .Select(r => new { r.Id, r.Status })
            .ToListAsync(cancellationToken);

        var totalRooms = rooms.Count;
        var available = rooms.Count(r => r.Status == RoomStatus.Available);
        var occupied = rooms.Count(r => r.Status == RoomStatus.Occupied);
        var maintenance = rooms.Count(r => r.Status == RoomStatus.Maintenance);
        var inactive = rooms.Count(r => r.Status == RoomStatus.Inactive);

        var todayStart = today.UtcStartOfDay();
        var tomorrow = today.AddDays(1).UtcStartOfDay();

        var todayBookings = await db.Bookings.CountAsync(
            b => b.CreatedAt >= todayStart && b.CreatedAt < tomorrow, cancellationToken);

        var todayCheckIns = await db.Bookings.CountAsync(
            b => b.CheckInDate == today && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn),
            cancellationToken);

        var todayCheckOuts = await db.Bookings.CountAsync(
            b => b.CheckOutDate == today && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn),
            cancellationToken);

        var activeBookings = await db.Bookings.CountAsync(
            b => b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn,
            cancellationToken);

        var activity = await db.RoomAssignments
            .Where(ra => ra.ReleasedAt == null)
            .Join(db.Bookings.Where(b =>
                    b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn),
                ra => ra.BookingId, b => b.Id,
                (ra, b) => new
                {
                    ra.RoomId,
                    b.Id,
                    b.Status,
                    b.CheckInAt,
                    b.CheckOutAt,
                    b.ActualCheckOutAt
                })
            .ToListAsync(cancellationToken);

        var coveringNow = activity
            .Where(a => a.CheckInAt <= nowUtc
                && (a.CheckOutAt > nowUtc || (a.Status == BookingStatus.CheckedIn && a.ActualCheckOutAt == null)))
            .ToList();

        var occupiedNowRoomIds = coveringNow.Select(a => a.RoomId)
            .Union(rooms.Where(r => r.Status == RoomStatus.Occupied).Select(r => r.Id))
            .Distinct()
            .ToList();

        var inHouse = activity
            .Where(a => a.Status == BookingStatus.CheckedIn && a.ActualCheckOutAt == null)
            .Select(a => a.Id).Distinct().Count();

        var overdueCheckOuts = activity
            .Where(a => a.Status == BookingStatus.CheckedIn && a.ActualCheckOutAt == null && a.CheckOutAt < nowUtc)
            .Select(a => a.Id).Distinct().Count();

        var roomsFreeingToday = coveringNow
            .Where(a => clock.ToLocalDate(new DateTimeOffset(DateTime.SpecifyKind(a.CheckOutAt, DateTimeKind.Utc))) == today)
            .Select(a => a.RoomId).Distinct().Count();

        var operationalRooms = Math.Max(0, totalRooms - maintenance - inactive);
        var occupiedNow = occupiedNowRoomIds.Count;
        var availableNow = Math.Max(0, operationalRooms - occupiedNow);
        var occupancyPercentage = operationalRooms <= 0 ? 0 :
            (int)Math.Round(occupiedNow * 100m / operationalRooms, MidpointRounding.AwayFromZero);

        var statusCounts = await db.Bookings
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var bookingsByStatus = statusCounts
            .OrderBy(x => x.Status)
            .Select(x => new BookingStatusCount(x.Status.ToString(), x.Count))
            .ToList();

        var trendFrom = today.AddDays(-29);
        var trendStart = new DateTimeOffset(trendFrom.ToDateTime(TimeOnly.MinValue), lagosOffset).UtcDateTime;
        var trendEnd = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), lagosOffset).UtcDateTime;

        var payments = await db.Payments
            .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt != null && p.PaidAt >= trendStart && p.PaidAt < trendEnd)
            .Select(p => new { p.PaidAt, p.Amount })
            .ToListAsync(cancellationToken);

        var revenueByDay = payments
            .GroupBy(p => clock.ToLocalDate(
                new DateTimeOffset(DateTime.SpecifyKind(p.PaidAt!.Value, DateTimeKind.Utc))))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var revenueTrend = Enumerable.Range(0, 30)
            .Select(i => trendFrom.AddDays(i))
            .Select(d => new DailyRevenue(d, revenueByDay.GetValueOrDefault(d, 0m)))
            .ToList();

        var revenueToday = payments
            .Where(p => clock.ToLocalDate(
                new DateTimeOffset(DateTime.SpecifyKind(p.PaidAt!.Value, DateTimeKind.Utc))) == today)
            .Sum(p => p.Amount);

        return new DashboardResponse(
            totalRooms, available, occupied, maintenance, inactive,
            todayBookings, todayCheckIns, todayCheckOuts, activeBookings,
            revenueToday, occupancyPercentage,
            availableNow, inHouse, overdueCheckOuts, roomsFreeingToday,
            bookingsByStatus, revenueTrend);
    }

    public async Task<Result<ReportResponse>> GetReportAsync(ReportFilterRequest request, CancellationToken cancellationToken)
    {
        var to = request.To ?? clock.Today;
        var from = request.From ?? to.AddDays(-29);

        if (to < from)
            return Error.Validation("report.invalid_range", "To must be on or after From");

        var days = (to.DayNumber - from.DayNumber) + 1;

        var activeRoomTypes = await db.Rooms.CountAsync(r => !r.IsDeleted && r.IsActive, cancellationToken);

        var bookings = await db.Bookings
            .Where(b => b.CheckInDate <= to && b.CheckOutDate >= from
                && b.Status != BookingStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var bookingIds = bookings.Select(b => b.Id).ToList();

        var lines = await db.BookingRoomTypes
            .Where(brt => bookingIds.Contains(brt.BookingId))
            .ToListAsync(cancellationToken);

        var roomNightsSold = lines.Sum(l => (long)l.Quantity * l.NumberOfNights);
        var bookingsCount = bookings.Count;
        var nightsBooked = bookings.Sum(b => b.Nights);
        var roomsBooked = lines.Sum(l => l.Quantity);

        decimal bookedTotal = bookings.Sum(b => b.TotalAmount);
        decimal paidTotal = await db.Payments
            .Where(p => p.Status == PaymentStatus.Paid && bookingIds.Contains(p.BookingId))
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var revenue = paidTotal;
        var outstanding = Math.Max(0, bookedTotal - paidTotal);

        var cancelled = await db.Bookings.CountAsync(
            b => b.Status == BookingStatus.Cancelled
                && b.CancelledAt != null
                && b.CancelledAt >= from.UtcStartOfDay()
                && b.CancelledAt < to.AddDays(1).UtcStartOfDay(),
            cancellationToken);

        var roomNightsCapacity = days * activeRoomTypes;
        var occupancyRate = roomNightsCapacity <= 0 ? 0m :
            Math.Round(roomNightsSold * 100m / roomNightsCapacity, 2, MidpointRounding.AwayFromZero);

        var daily = new List<DailyMetric>(days);
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var dayBookings = bookings.Count(b => b.CheckInDate == d && b.Status != BookingStatus.Cancelled);
            var dayRevenue = await db.Payments
                .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt != null
                    && p.PaidAt >= d.UtcStartOfDay()
                    && p.PaidAt < d.AddDays(1).UtcStartOfDay())
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

            daily.Add(new DailyMetric(d, dayBookings, dayRevenue));
        }

        return new ReportResponse(
            from, to, bookingsCount, nightsBooked, roomsBooked,
            revenue, outstanding, cancelled,
            (int)roomNightsSold, roomNightsCapacity, occupancyRate, daily);
    }
}