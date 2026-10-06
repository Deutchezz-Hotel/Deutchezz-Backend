using Deutchezz.Application.Common;
using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Notifications;

public interface INotificationService
{
    Task<Result<PaginatedResult<NotificationResponse>>> ListAsync(Guid userId, NotificationQuery query, CancellationToken cancellationToken);
    Task<Result> MarkAsReadAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<Result<int>> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken);
}