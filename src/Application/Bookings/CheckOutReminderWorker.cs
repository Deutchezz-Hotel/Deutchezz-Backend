using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Application.Emails;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deutchezz.Application.Bookings;

/// <summary>
/// v1.16: emails the front desk shortly before a checked-in booking is due to end, so staff can
/// warn the guest and prepare the check-out. Runs every <c>Jobs:CheckOutReminderIntervalMinutes</c>
/// minutes; the lead time is the <c>CheckOutReminderLeadMinutes</c> setting (default 30). Each
/// booking is reminded once (guarded by <see cref="Domain.Entities.Booking.CheckOutReminderSentAt"/>,
/// which a stay change clears).
/// </summary>
public sealed class CheckOutReminderWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<JobsOptions> jobs,
    IOptions<EmailOptions> emailOptions,
    ILogger<CheckOutReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, jobs.Value.CheckOutReminderIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RemindAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Check-out reminder run failed");
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

    private async Task RemindAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
        var businessClock = scope.ServiceProvider.GetRequiredService<IBusinessClock>();

        var leadMinutes = await ReadLeadMinutesAsync(db, settings, cancellationToken);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var horizon = nowUtc.AddMinutes(leadMinutes);

        var due = await db.Bookings
            .Include(b => b.Guest)
            .Where(b => b.Status == BookingStatus.CheckedIn
                && b.CheckOutReminderSentAt == null
                && b.CheckOutAt <= horizon)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
            return;

        var recipients = await ReadRecipientsAsync(
            db, settings, emailOptions.Value.AdminNotificationAddress, cancellationToken);
        if (recipients.Count == 0)
        {
            logger.LogWarning(
                "Skipped {Count} check-out reminder(s): no reception inbox configured " +
                "(set ReceptionNotificationEmails or Email:AdminNotificationAddress)",
                due.Count);
            return;
        }

        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var lagosOffset = businessClock.ConvertFromUtc(businessClock.UtcNow).Offset;

        foreach (var booking in due)
        {
            var roomNumbers = await RoomNumbersAsync(db, booking.Id, cancellationToken);
            var guestName = booking.Guest is null
                ? "Guest"
                : $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim();

            var (subject, html, text) = CheckOutReminderEmail.Build(new CheckOutReminderModel(
                booking.BookingReference,
                guestName,
                booking.Guest?.PhoneNumber,
                roomNumbers,
                booking.CheckOutDate,
                booking.CheckOutLocalTime(lagosOffset),
                CheckOutReminderPolicy.MinutesUntilCheckOut(booking.CheckOutAt, nowUtc)));

            foreach (var recipient in recipients)
                await emailSender.SendAsync(recipient, subject, html, text, CancellationToken.None);

            booking.CheckOutReminderSentAt = nowUtc;
        }

        await db.SaveChangesAsync(cancellationToken);

        var activityLogger = scope.ServiceProvider.GetRequiredService<IActivityLogger>();
        foreach (var booking in due)
        {
            await activityLogger.LogAsync(null, "booking.checkout_reminder_sent", "Booking", booking.Id,
                $"Check-out reminder emailed to {string.Join(", ", recipients)}", cancellationToken);
        }

        logger.LogInformation("Sent {Count} check-out reminder(s)", due.Count);
    }

    private static async Task<int> ReadLeadMinutesAsync(
        IAppDbContext db, ISettingsProvider settings, CancellationToken cancellationToken)
    {
        var stored = await db.Settings
            .FirstOrDefaultAsync(s => s.Key == "CheckOutReminderLeadMinutes", cancellationToken);
        return stored is not null && int.TryParse(stored.Value, out var parsed) && parsed is >= 5 and <= 2880
            ? parsed
            : settings.CheckOutReminderLeadMinutes;
    }

    private static async Task<IReadOnlyList<string>> ReadRecipientsAsync(
        IAppDbContext db, ISettingsProvider settings, string adminNotificationAddress,
        CancellationToken cancellationToken)
    {
        var stored = await db.Settings
            .FirstOrDefaultAsync(s => s.Key == "ReceptionNotificationEmails", cancellationToken);

        var configured = string.IsNullOrWhiteSpace(stored?.Value)
            ? settings.ReceptionNotificationEmails
            : stored!.Value;

        return CheckOutReminderPolicy.ResolveRecipients(configured, adminNotificationAddress);
    }

    private static async Task<IReadOnlyList<string>> RoomNumbersAsync(
        IAppDbContext db, Guid bookingId, CancellationToken cancellationToken)
    {
        var roomIds = await db.RoomAssignments
            .Where(ra => ra.BookingId == bookingId && ra.ReleasedAt == null)
            .Select(ra => ra.RoomId)
            .ToListAsync(cancellationToken);

        if (roomIds.Count == 0)
            return [];

        return await db.Rooms
            .Where(r => roomIds.Contains(r.Id))
            .OrderBy(r => r.RoomNumber)
            .Select(r => r.RoomNumber)
            .ToListAsync(cancellationToken);
    }
}
