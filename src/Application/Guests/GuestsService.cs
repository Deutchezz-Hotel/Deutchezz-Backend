using System.Linq.Expressions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Guests;

public record GuestResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    Guid? UserId,
    DateTime CreatedAt);

public class GuestQuery : QueryParameters
{
    private string? _search;

    public string? Search { get => _search; set => _search = value?.Trim(); }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}

public interface IGuestsService
{
    Task<Result<PaginatedResult<GuestResponse>>> ListAsync(GuestQuery query, CancellationToken cancellationToken);
    Task<Result<GuestResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

public class GuestsService(IAppDbContext db) : IGuestsService
{
    private static readonly Dictionary<string, Expression<Func<Domain.Entities.Guest, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["firstName"] = g => g.FirstName,
            ["lastName"] = g => g.LastName,
            ["email"] = g => g.Email,
            ["createdAt"] = g => g.CreatedAt
        };

    public async Task<Result<PaginatedResult<GuestResponse>>> ListAsync(GuestQuery query, CancellationToken cancellationToken)
    {
        var q = db.Guests.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.ToLowerInvariant();
            q = q.Where(g => g.FirstName.ToLower().Contains(term)
                || g.LastName.ToLower().Contains(term)
                || g.Email.ToLower().Contains(term));
        }

        if (query.From is not null)
            q = q.Where(g => g.CreatedAt >= query.From.Value.UtcStartOfDay());
        if (query.To is not null)
            q = q.Where(g => g.CreatedAt < query.To.Value.AddDays(1).UtcStartOfDay());

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .ApplySort(query, Sortable, qq => qq.OrderByDescending(g => g.CreatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(g => new GuestResponse(g.Id, g.FirstName, g.LastName, g.Email, g.PhoneNumber, g.UserId, g.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<GuestResponse>(items, total, query.Page, query.PageSize);
    }

    public async Task<Result<GuestResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var guest = await db.Guests.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (guest is null)
            return Error.NotFound("guest.not_found", "Guest not found");

        return new GuestResponse(guest.Id, guest.FirstName, guest.LastName, guest.Email, guest.PhoneNumber, guest.UserId, guest.CreatedAt);
    }
}