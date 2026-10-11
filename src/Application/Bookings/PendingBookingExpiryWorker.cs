using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deutchezz.Application.Bookings;

/// <summary>
/// SoT 7e: automatically expires <see cref="BookingStatus.Pending"/> bookings that have stayed
/// Pending for longer than <c>Cancellation:WindowHours</c> since creation. Pay-at-hotel bookings
/// are created <see cref="BookingStatus.Confirmed"/> and are never touched. The cycle interval is
/// <c>Jobs:PendingExpiryIntervalMinutes</c> (default 15).
/// </summary>
public sealed class PendingBookingExpiryWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<JobsOptions> jobs,
    ILogger<PendingBookingExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, jobs.Value.PendingExpiryIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pending booking expiry run failed");
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

    private async Task ExpireAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();

        var stored = await db.Settings.FirstOrDefaultAsync(s => s.Key == "CancellationWindowHours", cancellationToken);
        var windowHours = stored is not null && int.TryParse(stored.Value, out var parsed) && parsed > 0
            ? parsed
            : settings.CancellationWindowHours;

        var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-windowHours);

        var expired = await db.Bookings
            .Where(b => b.Status == BookingStatus.Pending && b.CreatedAt <= cutoff)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
            return;

        var guestIds = expired.Select(b => b.GuestId).Distinct().ToList();
        var userIdByGuest = await db.Guests
            .Where(g => guestIds.Contains(g.Id) && g.UserId != null)
            .Select(g => new { g.Id, g.UserId })
            .ToDictionaryAsync(g => g.Id, g => g.UserId, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var booking in expired)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = now;
            booking.CancellationReason = "Expired reservation";
            booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

            if (userIdByGuest.TryGetValue(booking.GuestId, out var userId) && userId is not null)
            {
                db.Notifications.Add(new Notification
                {
                    GuestId = booking.GuestId,
                    UserId = userId,
                    Type = NotificationType.BookingCancelled,
                    Title = "Booking cancelled",
                    Message = $"Booking {booking.BookingReference} was cancelled because it was not confirmed in time.",
                    CreatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var activityLogger = scope.ServiceProvider.GetRequiredService<IActivityLogger>();
        foreach (var booking in expired)
        {
            await activityLogger.LogAsync(null, "booking.expired", "Booking", booking.Id,
                $"Pending booking {booking.BookingReference} expired", cancellationToken);
        }

        logger.LogInformation("Expired {Count} pending booking(s)", expired.Count);
    }
}