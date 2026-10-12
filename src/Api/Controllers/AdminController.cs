using Deutchezz.Api.Authorization;
using Deutchezz.Api.Extensions;
using Deutchezz.Application.ActivityLogs;
using Deutchezz.Application.Bookings;
using Deutchezz.Application.Guests;
using Deutchezz.Application.Notifications;
using Deutchezz.Application.Payments;
using Deutchezz.Application.Reports;
using Deutchezz.Application.Rooms;
using Deutchezz.Application.RoomTypes;
using Deutchezz.Application.Settings;
using Deutchezz.Application.Staff;
using Deutchezz.Application.Users;
using Deutchezz.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Policies.AdminOnly)]
public class AdminController(
    IRoomsService rooms,
    IRoomTypesService roomTypes,
    IBookingService bookings,
    IGuestsService guests,
    IStaffService staff,
    IUserAdminService users,
    IPaymentService payments,
    INotificationService notifications,
    IAdminSettingsService settings,
    IReportsService reports,
    IActivityLogsService activityLogs) : ControllerBase
{
    // ----- Rooms -----

    [HttpGet("rooms")]
    public async Task<IActionResult> ListRooms([FromQuery] RoomQuery query, CancellationToken cancellationToken)
    {
        var result = await rooms.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("rooms/{id:guid}")]
    public async Task<IActionResult> GetRoom(Guid id, CancellationToken cancellationToken)
    {
        var result = await rooms.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("rooms")]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await rooms.CreateAsync(request, userId.Value, cancellationToken);
        if (result.IsFailure) return result.ToActionResult();
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("rooms/{id:guid}")]
    public async Task<IActionResult> UpdateRoom(Guid id, [FromBody] UpdateRoomRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await rooms.UpdateAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPatch("rooms/{id:guid}/status")]
    public async Task<IActionResult> SetRoomStatus(Guid id, [FromBody] SetRoomStatusRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await rooms.SetStatusAsync(id, request.Status, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Room types -----

    [HttpGet("room-types")]
    public async Task<IActionResult> ListRoomTypes([FromQuery] RoomTypeQuery query, CancellationToken cancellationToken)
    {
        var result = await roomTypes.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("room-types/{id:guid}")]
    public async Task<IActionResult> GetRoomType(Guid id, CancellationToken cancellationToken)
    {
        var result = await roomTypes.GetByIdForAdminAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("room-types")]
    public async Task<IActionResult> CreateRoomType([FromBody] CreateRoomTypeRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await roomTypes.CreateAsync(request, userId.Value, cancellationToken);
        if (result.IsFailure) return result.ToActionResult();
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("room-types/{id:guid}")]
    public async Task<IActionResult> UpdateRoomType(Guid id, [FromBody] UpdateRoomTypeRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await roomTypes.UpdateAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPatch("room-types/{id:guid}/status")]
    public async Task<IActionResult> SetRoomTypeStatus(Guid id, [FromBody] SetRoomTypeStatusRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await roomTypes.SetActiveAsync(id, request.IsActive, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Bookings -----

    [HttpGet("bookings")]
    public async Task<IActionResult> ListBookings([FromQuery] BookingSearchQuery query, CancellationToken cancellationToken)
    {
        var result = await bookings.SearchAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Guests -----

    [HttpGet("guests")]
    public async Task<IActionResult> ListGuests([FromQuery] GuestQuery query, CancellationToken cancellationToken)
    {
        var result = await guests.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Users -----

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers([FromQuery] UserAdminQuery query, CancellationToken cancellationToken)
    {
        var result = await users.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPatch("users/{id:guid}/role")]
    public async Task<IActionResult> ChangeUserRole(Guid id, [FromBody] ChangeUserRoleRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await users.ChangeRoleAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Staff -----

    [HttpGet("staff")]
    public async Task<IActionResult> ListStaff([FromQuery] StaffQuery query, CancellationToken cancellationToken)
    {
        var result = await staff.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("staff/{id:guid}")]
    public async Task<IActionResult> GetStaff(Guid id, CancellationToken cancellationToken)
    {
        var result = await staff.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("staff")]
    public async Task<IActionResult> CreateStaff([FromBody] CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await staff.CreateAsync(request, userId.Value, cancellationToken);
        if (result.IsFailure) return result.ToActionResult();
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("staff/{id:guid}")]
    public async Task<IActionResult> UpdateStaff(Guid id, [FromBody] UpdateStaffRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await staff.UpdateAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPatch("staff/{id:guid}/status")]
    public async Task<IActionResult> SetStaffStatus(Guid id, [FromBody] SetStaffStatusRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await staff.SetActiveAsync(id, request.IsActive, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Payments -----

    [HttpGet("payments")]
    public async Task<IActionResult> ListPayments([FromQuery] PaymentQuery query, CancellationToken cancellationToken)
    {
        var result = await payments.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Notifications -----

    [HttpGet("notifications")]
    public async Task<IActionResult> ListNotifications([FromQuery] NotificationQuery query, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await notifications.ListAsync(userId.Value, query, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Settings -----

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var result = await settings.GetAsync(cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateAdminSettingsRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await settings.UpdateAsync(request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    // ----- Dashboard / Reports / Activity logs -----

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var result = await reports.GetDashboardAsync(cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReport([FromQuery] ReportFilterRequest request, CancellationToken cancellationToken)
    {
        var result = await reports.GetReportAsync(request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("activity-logs")]
    public async Task<IActionResult> ListActivityLogs([FromQuery] ActivityLogQuery query, CancellationToken cancellationToken)
    {
        var result = await activityLogs.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }
}