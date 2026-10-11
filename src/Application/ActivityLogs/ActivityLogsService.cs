using System.Linq.Expressions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.ActivityLogs;

public record ActivityLogResponse(
    Guid Id,
    Guid? UserId,
    string? UserName,
    string Action,
    string EntityType,
    Guid? EntityId,
    string? Description,
    DateTime CreatedAt);

public class ActivityLogQuery : QueryParameters
{
    private string? _action;
    private string? _entityType;

    public string? Action { get => _action; set => _action = value?.Trim(); }

    public string? EntityType { get => _entityType; set => _entityType = value?.Trim(); }

    public Guid? UserId { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}

public interface IActivityLogsService
{
    Task<Result<PaginatedResult<ActivityLogResponse>>> ListAsync(ActivityLogQuery query, CancellationToken cancellationToken);
}

public class ActivityLogsService(IAppDbContext db) : IActivityLogsService
{
    private static readonly Dictionary<string, Expression<Func<Domain.Entities.ActivityLog, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = a => a.CreatedAt,
            ["action"] = a => a.Action,
            ["entityType"] = a => a.EntityType
        };

    public async Task<Result<PaginatedResult<ActivityLogResponse>>> ListAsync(ActivityLogQuery query, CancellationToken cancellationToken)
    {
        var q = db.ActivityLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.EntityType))
            q = q.Where(a => a.EntityType == query.EntityType);
        if (query.UserId is not null)
            q = q.Where(a => a.UserId == query.UserId);
        if (query.From is not null)
            q = q.Where(a => a.CreatedAt >= query.From.Value.UtcStartOfDay());
        if (query.To is not null)
            q = q.Where(a => a.CreatedAt < query.To.Value.AddDays(1).UtcStartOfDay());

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .ApplySort(query, Sortable, qq => qq.OrderByDescending(a => a.CreatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new ActivityLogResponse(
                a.Id, a.UserId, null, a.Action, a.EntityType, a.EntityId, a.Description, a.CreatedAt))
            .ToListAsync(cancellationToken);

        var actorIds = items.Where(i => i.UserId is not null).Select(i => i.UserId!.Value).Distinct().ToList();
        var names = await db.Users
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new UserNameKey(u.Id, (u.FirstName + " " + u.LastName).Trim()))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        var mapped = items
            .Select(i => i with { UserName = i.UserId is not null && names.TryGetValue(i.UserId.Value, out var name) ? name : null })
            .ToList();

        return new PaginatedResult<ActivityLogResponse>(mapped, total, query.Page, query.PageSize);
    }

    private sealed record UserNameKey(Guid Id, string Name);
}