using System.Linq.Expressions;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Constants;
using Deutchezz.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Staff;

public record CreateStaffRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string Role,
    string? PhoneNumber);

public record UpdateStaffRequest(string? FirstName, string? LastName, string? PhoneNumber, string? Role);

public record SetStaffStatusRequest(bool IsActive);

public record StaffResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string Role,
    bool IsActive,
    DateTime CreatedAt);

public class StaffQuery : QueryParameters
{
    public bool? IsActive { get; set; }
    private string? _role;
    public string? Role { get => _role; set => _role = value?.Trim(); }
}

public interface IStaffService
{
    Task<Result<PaginatedResult<StaffResponse>>> ListAsync(StaffQuery query, CancellationToken cancellationToken);
    Task<Result<StaffResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<StaffResponse>> CreateAsync(CreateStaffRequest request, Guid adminUserId, CancellationToken cancellationToken);
    Task<Result<StaffResponse>> UpdateAsync(Guid id, UpdateStaffRequest request, Guid adminUserId, CancellationToken cancellationToken);
    Task<Result> SetActiveAsync(Guid id, bool isActive, Guid adminUserId, CancellationToken cancellationToken);
}

public class StaffService(
    IAppDbContext db,
    UserManager<User> userManager,
    IActivityLogger activityLogger) : IStaffService
{
    private static readonly string[] AllowedRoles = [Roles.Receptionist, Roles.Administrator];

    private static readonly Dictionary<string, Expression<Func<User, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["firstName"] = u => u.FirstName!,
            ["lastName"] = u => u.LastName!,
            ["email"] = u => u.Email!,
            ["isActive"] = u => u.IsActive,
            ["createdAt"] = u => u.CreatedAt
        };

    public async Task<Result<PaginatedResult<StaffResponse>>> ListAsync(StaffQuery query, CancellationToken cancellationToken)
    {
        // The staff section only ever shows users holding a staff role; customers belong to the
        // users directory (`GET /api/v1/admin/users`).
        var staffRoles = await db.Roles
            .Where(r => r.Name == Roles.Receptionist || r.Name == Roles.Administrator)
            .ToDictionaryAsync(r => r.Name!, r => r.Id, cancellationToken);

        var staffRoleIds = staffRoles.Values.ToList();
        var q = db.Users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && staffRoleIds.Contains(ur.RoleId)));

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            if (!staffRoles.TryGetValue(CanonicalRole(query.Role), out var roleId))
                return new PaginatedResult<StaffResponse>([], 0, query.Page, query.PageSize);

            q = q.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }

        if (query.IsActive is not null)
            q = q.Where(u => u.IsActive == query.IsActive);

        var total = await q.CountAsync(cancellationToken);

        var users = await q
            .ApplySort(query, Sortable, qq => qq.OrderByDescending(u => u.CreatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = new List<StaffResponse>(users.Count);
        foreach (var user in users)
        {
            items.Add(await ToResponseAsync(user, cancellationToken).ConfigureAwait(false));
        }

        return new PaginatedResult<StaffResponse>(items, total, query.Page, query.PageSize);
    }

    public async Task<Result<StaffResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
            return Error.NotFound("staff.not_found", "Staff member not found");

        return await ToResponseAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<StaffResponse>> CreateAsync(CreateStaffRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Error.Validation("staff.invalid_input", "Email and password are required");
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return Error.Validation("staff.name_required", "First and last name are required");
        if (!AllowedRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
            return Error.Validation("staff.invalid_role", "Role must be Receptionist or Administrator");
        if (request.Password.Length < 8)
            return Error.Validation("staff.weak_password", "Password must be at least 8 characters");

        if (await userManager.FindByEmailAsync(request.Email) is not null)
            return Error.Conflict("staff.email_in_use", "Email is already registered");

        if (!PhoneNumber.TryNormalize(request.PhoneNumber, out var phone))
            return Error.Validation("auth.invalid_phone",
                "Enter a valid phone number, for example 0803 123 4567 or +234 803 123 4567");

        if (phone is not null && await db.Users.AnyAsync(u => u.PhoneNumber == phone, cancellationToken))
            return Error.Validation("auth.phone_in_use", "That phone number can't be used.");

        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = phone,
            IsActive = true,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Error.Validation("staff.creation_failed", string.Join(", ", result.Errors.Select(e => e.Description)));

        var role = request.Role[..1].ToUpperInvariant() + request.Role[1..].ToLowerInvariant();
        await userManager.AddToRoleAsync(user, role);

        await activityLogger.LogAsync(adminUserId, "staff.created", "User", user.Id,
            $"Created staff {request.Email} with role {role}", cancellationToken);

        return await ToResponseAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<StaffResponse>> UpdateAsync(Guid id, UpdateStaffRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Error.NotFound("staff.not_found", "Staff member not found");

        if (request.FirstName is not null)
            user.FirstName = request.FirstName;
        if (request.LastName is not null)
            user.LastName = request.LastName;
        if (request.PhoneNumber is not null)
        {
            if (!PhoneNumber.TryNormalize(request.PhoneNumber, out var phone))
                return Error.Validation("auth.invalid_phone",
                    "Enter a valid phone number, for example 0803 123 4567 or +234 803 123 4567");

            if (phone is not null && await db.Users.AnyAsync(u => u.PhoneNumber == phone && u.Id != id, cancellationToken))
                return Error.Validation("auth.phone_in_use", "That phone number can't be used.");

            user.PhoneNumber = phone;
        }

        if (request.Role is not null)
        {
            if (!AllowedRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
                return Error.Validation("staff.invalid_role", "Role must be Receptionist or Administrator");

            var currentRoles = await userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
                await userManager.RemoveFromRolesAsync(user, currentRoles);

            var role = request.Role[..1].ToUpperInvariant() + request.Role[1..].ToLowerInvariant();
            await userManager.AddToRoleAsync(user, role);
        }

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return Error.Validation("staff.update_failed", string.Join(", ", update.Errors.Select(e => e.Description)));

        await activityLogger.LogAsync(adminUserId, "staff.updated", "User", user.Id, "Updated staff", cancellationToken);

        return await ToResponseAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, Guid adminUserId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Error.NotFound("staff.not_found", "Staff member not found");

        if (!isActive && user.IsActive)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (roles.Contains(Roles.Administrator)
                && await CountActiveAdministratorsAsync(excluding: user.Id, cancellationToken) == 0)
                return Error.Conflict("user.last_administrator",
                    "The last active administrator cannot be deactivated.");
        }

        user.IsActive = isActive;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return Error.Validation("staff.update_failed", string.Join(", ", update.Errors.Select(e => e.Description)));

        await activityLogger.LogAsync(adminUserId, "staff.status_changed", "User", user.Id,
            isActive ? "Staff activated" : "Staff deactivated", cancellationToken);

        return Result.Success();
    }

    private async Task<StaffResponse> ToResponseAsync(User user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? Roles.Customer;
        return new StaffResponse(user.Id, user.FirstName ?? "", user.LastName ?? "", user.Email ?? "",
            user.PhoneNumber, role, user.IsActive, user.CreatedAt);
    }

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

    private static string CanonicalRole(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "receptionist" => Roles.Receptionist,
            "administrator" => Roles.Administrator,
            _ => role.Trim()
        };
}