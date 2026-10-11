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
    int OccupancyPercentage);

public record ReportFilterRequest(DateOnly? From, DateOnly? To);

public record DailyMetric(DateOnly Date, int Bookings, decimal Revenue);

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

        var totalRooms = await db.Rooms.CountAsync(r => !r.IsDeleted, cancellationToken);
        var available = await db.Rooms.CountAsync(r => r.Status == RoomStatus.Available, cancellationToken);
        var occupied = await db.Rooms.CountAsync(r => r.Status == RoomStatus.Occupied, cancellationToken);
        var maintenance = await db.Rooms.CountAsync(r => r.Status == RoomStatus.Maintenance, cancellationToken);
        var inactive = await db.Rooms.CountAsync(r => r.Status == RoomStatus.Inactive, cancellationToken);

        var todayStart = today.UtcStartOfDay();
        var tomorrow = today.AddDays(1).UtcStartOfDay();

        var todayBookings = await db.Bookings.CountAsync(
            b => b.CreatedAt >= todayStart && b.CreatedAt < tomorrow, cancellationToken);

        var todayCheckIns = await db.Bookings.CountAsync(
            b => b.CheckInDate == today && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn),
            cancellationToken);

        var todayCheckOuts = await db.Bookings.CountAsync(
            b => b.CheckOutDate == today && b.Status == BookingStatus.CheckedOut, cancellationToken);

        var activeBookings = await db.Bookings.CountAsync(
            b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn, cancellationToken);

        var revenueToday = await db.Payments
            .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt != null && p.PaidAt >= todayStart && p.PaidAt < tomorrow)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var operationalRooms = Math.Max(1, totalRooms - inactive);
        var occupancyPercentage = (int)Math.Round(occupied * 100m / operationalRooms, MidpointRounding.AwayFromZero);

        return new DashboardResponse(
            totalRooms, available, occupied, maintenance, inactive,
            todayBookings, todayCheckIns, todayCheckOuts, activeBookings, revenueToday, occupancyPercentage);
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