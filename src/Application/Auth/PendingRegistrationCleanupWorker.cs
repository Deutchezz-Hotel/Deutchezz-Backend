using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deutchezz.Application.Auth;

/// <summary>
/// Purges expired <c>PendingRegistration</c> rows and stale <c>PasswordResetCode</c> rows so
/// unverified sign-ups never accumulate (BR-026). Runs every
/// <c>Jobs:VerificationCleanupIntervalMinutes</c> minutes.
/// </summary>
public sealed class PendingRegistrationCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<JobsOptions> jobs,
    ILogger<PendingRegistrationCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, jobs.Value.VerificationCleanupIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pending registration cleanup run failed");
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

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var cutoff = now.AddHours(-24);

        var pending = await db.PendingRegistrations
            .Where(p => p.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        var codes = await db.PasswordResetCodes
            .Where(c => (c.UsedAt != null && c.UsedAt <= cutoff) || (c.UsedAt == null && c.ExpiresAt <= cutoff))
            .ToListAsync(cancellationToken);

        if (pending.Count == 0 && codes.Count == 0)
            return;

        db.PendingRegistrations.RemoveRange(pending);
        db.PasswordResetCodes.RemoveRange(codes);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Cleaned up {PendingCount} pending registration(s) and {CodeCount} password reset code(s)",
            pending.Count, codes.Count);
    }
}
