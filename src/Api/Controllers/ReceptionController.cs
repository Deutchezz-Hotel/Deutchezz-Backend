using Deutchezz.Api.Authorization;
using Deutchezz.Api.Extensions;
using Deutchezz.Application.Bookings;
using Deutchezz.Application.Guests;
using Deutchezz.Application.Payments;
using Deutchezz.Application.Rooms;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace Deutchezz.Api.Controllers;

[ApiController]
[Route("api/v1/reception")]
[Authorize(Policy = Policies.StaffOnly)]
public class ReceptionController(
    IBookingService bookings,
    IRoomsService rooms,
    IGuestsService guests,
    IPaymentService payments) : ControllerBase
{
    [HttpGet("bookings")]
    public async Task<IActionResult> ListBookings([FromQuery] BookingSearchQuery query, CancellationToken cancellationToken)
    {
        var result = await bookings.SearchAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("bookings/{id:guid}")]
    public async Task<IActionResult> GetBooking(Guid id, CancellationToken cancellationToken)
    {
        var result = await bookings.GetDetailAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting("booking-create")]
    [HttpPost("bookings")]
    public async Task<IActionResult> CreateBooking([FromBody] ReceptionCreateBookingRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Error.Validation("idempotency_key_required", "The Idempotency-Key header is required").ToActionResult();

        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.ReceptionCreateAsync(request, userId.Value, idempotencyKey, cancellationToken);
        if (result.IsFailure)
            return result.ToActionResult();

        return result.Value.Replay
            ? Ok(result.Value)
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPost("bookings/{id:guid}/cancel")]
    public async Task<IActionResult> CancelBooking(Guid id, [FromBody] ReceptionCancelBookingRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.ReceptionCancelAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("bookings/{id:guid}/assign-rooms")]
    public async Task<IActionResult> AssignRooms(Guid id, [FromBody] RoomAssignmentRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.AssignRoomsAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("bookings/{id:guid}/check-in")]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.CheckInAsync(id, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("bookings/{id:guid}/check-out")]
    public async Task<IActionResult> CheckOut(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CheckOutRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var isAdministrator = this.GetRoles().Contains(Roles.Administrator);
        var result = await bookings.CheckOutAsync(id, request, userId.Value, isAdministrator, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("bookings/{id:guid}/check-out-quote")]
    public async Task<IActionResult> QuoteCheckOut(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LateCheckOutQuoteRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.QuoteLateCheckOutAsync(id, request?.ProposedCheckOutUtc, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("bookings/{id:guid}/change-room-type")]
    public async Task<IActionResult> ChangeRoomType(Guid id, [FromBody] ChangeRoomTypeRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.ChangeRoomTypeAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("bookings/{id:guid}/reassign-room")]
    public async Task<IActionResult> ReassignRoom(Guid id, [FromBody] ReassignRoomRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.ReassignRoomAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("bookings/{id:guid}/unassign-room")]
    public async Task<IActionResult> UnassignRoom(Guid id, [FromBody] UnassignRoomRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.UnassignRoomAsync(id, request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("rooms")]
    public async Task<IActionResult> ListRooms([FromQuery] RoomQuery query, CancellationToken cancellationToken)
    {
        var result = await rooms.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("guests")]
    public async Task<IActionResult> ListGuests([FromQuery] GuestQuery query, CancellationToken cancellationToken)
    {
        var result = await guests.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("payments")]
    public async Task<IActionResult> ListPayments([FromQuery] PaymentQuery query, CancellationToken cancellationToken)
    {
        var result = await payments.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("payments")]
    public async Task<IActionResult> RecordPayment([FromBody] RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await payments.RecordAsync(request, userId.Value, cancellationToken);
        return result.ToActionResult();
    }
}