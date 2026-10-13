using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deutchezz.Application.Bookings;

/// <summary>
/// SoT 7n / BR-042: permanently removes cancelled bookings once they are older than
/// <c>BookingRetention:CancelledDays</c> (measured from <c>CancelledAt</c>). Runs on start-up and
/// then every <c>BookingRetention:IntervalMinutes</c> minutes.
///
/// <para>Safety rules:
/// <list type="bullet">
/// <item>A cancelled booking that has ANY payments is never purged; it is logged for a refund
/// review (a payment must be refunded/reconciled by a human first).</item>
/// <item>Dependent rows (<c>BookingRoomTypes</c>, <c>RoomAssignments</c>) are deleted explicitly
/// inside the same transaction — they have no FK cascade to <c>Bookings</c>.</item>
/// <item>Each cycle is idempotent and isolated: a failure is logged and never stops the host.</item>
/// </list></para>
/// </summary>
public sealed class CancelledBookingPurgeWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BookingRetentionOptions> retention,
    ILogger<CancelledBookingPurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, retention.Value.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cancelled booking purge run failed");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        if (!retention.Value.Enabled)
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var cutoff = clock.UtcNow.AddDays(-retention.Value.CancelledDays);

        var candidates = await db.Bookings
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.Cancelled
                && b.CancelledAt != null && b.CancelledAt <= cutoff)
            .Select(b => new { b.Id, b.BookingReference })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return;

        var ids = candidates.Select(c => c.Id).ToList();

        var withPayments = await db.Payments
            .Where(p => ids.Contains(p.BookingId))
            .Select(p => p.BookingId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var blocked = withPayments.ToHashSet();

        if (blocked.Count > 0)
        {
            var references = string.Join(", ",
                candidates.Where(c => blocked.Contains(c.Id)).Select(c => c.BookingReference));
            logger.LogWarning(
                "{Count} cancelled booking(s) are past retention but have payments and need a refund review before they can be purged: {References}",
                blocked.Count, references);
        }

        var purgeable = ids.Where(id => !blocked.Contains(id)).ToList();
        if (purgeable.Count == 0)
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var bookingId in purgeable)
        {
            var assignments = await db.RoomAssignments
                .Where(ra => ra.BookingId == bookingId)
                .ToListAsync(cancellationToken);
            var lines = await db.BookingRoomTypes
                .Where(brt => brt.BookingId == bookingId)
                .ToListAsync(cancellationToken);

            db.RoomAssignments.RemoveRange(assignments);
            db.BookingRoomTypes.RemoveRange(lines);

            var booking = await db.Bookings
                .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
            if (booking is not null)
                db.Bookings.Remove(booking);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Permanently removed {Count} cancelled booking(s) past retention", purgeable.Count);
    }
}
