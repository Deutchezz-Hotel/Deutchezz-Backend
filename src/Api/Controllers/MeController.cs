using Deutchezz.Api.Authorization;
using Deutchezz.Api.Extensions;
using Deutchezz.Application.Bookings;
using Deutchezz.Application.Notifications;
using Deutchezz.Application.Profile;
using Deutchezz.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize(Policy = Policies.CustomerOnly)]
public class MeController(
    IBookingService bookings,
    IProfileService profile,
    INotificationService notifications) : ControllerBase
{
    [HttpGet("bookings")]
    public async Task<IActionResult> ListBookings([FromQuery] BookingSearchQuery query, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.ListForCustomerAsync(userId.Value, query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("bookings/{id:guid}")]
    public async Task<IActionResult> GetBooking(Guid id, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.GetByIdForOwnerAsync(id, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("bookings/{id:guid}/cancel")]
    public async Task<IActionResult> CancelBooking(Guid id, [FromBody] CancelBookingRequest? request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.CancelAsync(id, null, request?.Reason, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await profile.GetAsync(userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await profile.UpdateAsync(userId.Value, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> ListNotifications([FromQuery] NotificationQuery query, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await notifications.ListAsync(userId.Value, query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("notifications/unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await notifications.GetUnreadCountAsync(userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkNotificationRead(Guid id, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null) return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await notifications.MarkAsReadAsync(id, userId.Value, cancellationToken);
        return result.ToActionResult();
    }
}