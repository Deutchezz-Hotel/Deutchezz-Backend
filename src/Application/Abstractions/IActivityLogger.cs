namespace Deutchezz.Application.Abstractions;

public interface IActivityLogger
{
    Task LogAsync(Guid? actorUserId, string action, string entityType, Guid? entityId, string? details, CancellationToken cancellationToken);
}