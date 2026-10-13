using System.Linq.Expressions;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Application.Emails;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Constants;
using Deutchezz.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Users;

public record UserListItemResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string Role,
    bool IsActive,
    bool EmailConfirmed,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    int BookingsCount);

public record ChangeUserRoleRequest(string Role);

public class UserAdminQuery : QueryParameters
{
    private string? _search;
    private string? _role;

    public string? Search { get => _search; set => _search = value?.Trim(); }

    /// <summary>Role filter; defaults to <c>Customer</c> when omitted.</summary>
    public string? Role { get => _role; set => _role = value?.Trim(); }
}

public interface IUserAdminService
{
    Task<Result<PaginatedResult<UserListItemResponse>>> ListAsync(UserAdminQuery query, CancellationToken cancellationToken);
    Task<Result<UserListItemResponse>> ChangeRoleAsync(Guid id, ChangeUserRoleRequest request, Guid adminUserId, CancellationToken cancellationToken);
}

public class UserAdminService(
    IAppDbContext db,
    UserManager<User> userManager,
    IActivityLogger activityLogger,
    IEmailSender emailSender) : IUserAdminService
{
    private static readonly Dictionary<string, Expression<Func<User, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["firstName"] = u => u.FirstName!,
            ["lastName"] = u => u.LastName!,
            ["email"] = u => u.Email!,
            ["isActive"] = u => u.IsActive,
            ["createdAt"] = u => u.CreatedAt,
            ["lastLoginAt"] = u => u.LastLoginAt!
        };

    public async Task<Result<PaginatedResult<UserListItemResponse>>> ListAsync(UserAdminQuery query, CancellationToken cancellationToken)
    {
        // The users directory defaults to customers; the staff list is the place for staff.
        var role = string.IsNullOrWhiteSpace(query.Role) ? Roles.Customer : CanonicalRole(query.Role);
        if (role is null)
            return Empty(query);

        var roleId = await db.Roles
            .Where(r => r.Name == role)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (roleId is null)
            return Empty(query);

        var q = db.Users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId.Value));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.ToLowerInvariant();
            q = q.Where(u =>
                (u.FirstName != null && u.FirstName.ToLower().Contains(term)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(term)) ||
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(term)));
        }

        var total = await q.CountAsync(cancellationToken);

        var users = await q
            .ApplySort(query, Sortable, qq => qq.OrderByDescending(u => u.CreatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = await MapAsync(users, role, cancellationToken);
        return new PaginatedResult<UserListItemResponse>(items, total, query.Page, query.PageSize);
    }

    public async Task<Result<UserListItemResponse>> ChangeRoleAsync(Guid id, ChangeUserRoleRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        var role = CanonicalRole(request.Role);
        if (role is null)
            return Error.Validation("user.invalid_role", "Role must be Customer, Receptionist or Administrator");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
            return Error.NotFound("user.not_found", "User not found");

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentRole = currentRoles.FirstOrDefault() ?? Roles.Customer;

        if (string.Equals(currentRole, role, StringComparison.Ordinal))
            return await BuildItemAsync(user, role, cancellationToken);

        if (id == adminUserId)
            return Error.Conflict("user.self_role_change", "You cannot change your own role.");

        if (!user.IsActive && IsStaff(role))
            return Error.Conflict("user.inactive", "An inactive account cannot be given a staff role.");

        if (string.Equals(currentRole, Roles.Administrator, StringComparison.Ordinal)
            && await CountActiveAdministratorsAsync(excluding: user.Id, cancellationToken) == 0)
            return Error.Conflict("user.last_administrator", "The last active administrator cannot be demoted.");

        if (currentRoles.Count > 0)
            await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, role);

        // The new role must apply on the next sign-in, and stale tokens must stop working.
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        await userManager.UpdateSecurityStampAsync(user);
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "user.role_changed", "User", user.Id,
            $"Changed role for {user.Email} from {currentRole} to {role}", cancellationToken);

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var name = $"{user.FirstName} {user.LastName}".Trim();
            var (subject, html, text) = RoleChangedEmail.Build(
                new RoleChangedModel(string.IsNullOrWhiteSpace(name) ? "there" : name, role));
            await emailSender.SendAsync(user.Email, subject, html, text, CancellationToken.None);
        }

        return await BuildItemAsync(user, role, cancellationToken);
    }

    private async Task<List<UserListItemResponse>> MapAsync(List<User> users, string role, CancellationToken cancellationToken)
    {
        if (users.Count == 0)
            return [];

        var ids = users.Select(u => u.Id).ToList();

        var counts = await (from b in db.Bookings
                            join g in db.Guests on b.GuestId equals g.Id
                            where g.UserId != null && ids.Contains(g.UserId.Value)
                            group b by g.UserId!.Value
                            into grp
                            select new { UserId = grp.Key, Count = grp.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        return users.Select(u => ToResponse(u, role, counts.TryGetValue(u.Id, out var count) ? count : 0)).ToList();
    }

    private async Task<UserListItemResponse> BuildItemAsync(User user, string role, CancellationToken cancellationToken)
    {
        var count = await db.Bookings
            .CountAsync(b => db.Guests.Any(g => g.Id == b.GuestId && g.UserId == user.Id), cancellationToken);
        return ToResponse(user, role, count);
    }

    private static UserListItemResponse ToResponse(User user, string role, int bookingsCount) =>
        new(user.Id, user.FirstName ?? "", user.LastName ?? "", user.Email ?? "", user.PhoneNumber,
            role, user.IsActive, user.EmailConfirmed, user.CreatedAt, user.LastLoginAt, bookingsCount);

    private async Task<int> CountActiveAdministratorsAsync(Guid? excluding, CancellationToken cancellationToken)
    {
        var adminRoleId = await db.Roles
            .Where(r => r.Name == Roles.Administrator)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (adminRoleId is null)
            return 0;

        return await db.Users.CountAsync(u => u.IsActive
            && (excluding == null || u.Id != excluding.Value)
            && db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId.Value), cancellationToken);
    }

    private static bool IsStaff(string role) =>
        role == Roles.Receptionist || role == Roles.Administrator;

    private static string? CanonicalRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "customer" => Roles.Customer,
        "receptionist" => Roles.Receptionist,
        "administrator" => Roles.Administrator,
        _ => null
    };

    private static PaginatedResult<UserListItemResponse> Empty(UserAdminQuery query) =>
        new([], 0, query.Page, query.PageSize);
}
