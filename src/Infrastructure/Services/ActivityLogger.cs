using Deutchezz.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Deutchezz.Infrastructure.Services;

public class ActivityLogger(Deutchezz.Application.Data.IAppDbContext context) : IActivityLogger
{
    public async Task LogAsync(Guid? actorUserId, string action, string entityType, Guid? entityId, string? details, CancellationToken cancellationToken)
    {
        context.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}