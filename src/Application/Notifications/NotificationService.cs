using System.Linq.Expressions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Notifications;

public class NotificationService(IAppDbContext db) : INotificationService
{
    private static readonly Dictionary<string, Expression<Func<Domain.Entities.Notification, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = n => n.CreatedAt,
            ["isRead"] = n => n.IsRead,
            ["type"] = n => n.Type
        };

    public async Task<Result<PaginatedResult<NotificationResponse>>> ListAsync(Guid userId, NotificationQuery query, CancellationToken cancellationToken)
    {
        var q = db.Notifications.Where(n => n.UserId == userId);

        if (query.IsRead is not null)
            q = q.Where(n => n.IsRead == query.IsRead);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .ApplySort(query, Sortable, qq => qq.OrderByDescending(n => n.CreatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(n => new NotificationResponse(n.Id, n.Type.ToString(), n.Title, n.Message, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<NotificationResponse>(items, total, query.Page, query.PageSize);
    }

    public async Task<Result> MarkAsReadAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var notif = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (notif is null)
            return Error.NotFound("notification.not_found", "Notification not found");

        notif.IsRead = true;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<int>> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var count = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
        return count;
    }
}