using Deutchezz.Api.Authorization;
using Deutchezz.Api.Extensions;
using Deutchezz.Application.Bookings;
using Deutchezz.Application.Guests;
using Deutchezz.Application.Payments;
using Deutchezz.Application.Rooms;
using Deutchezz.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Error.Validation("idempotency_key_required", "The Idempotency-Key header is required").ToActionResult();

        var userId = this.GetUserId();
        var result = await bookings.CreateAsync(request, userId, idempotencyKey, cancellationToken);
        if (result.IsFailure)
            return result.ToActionResult();

        return result.Value.Replay
            ? Ok(result.Value)
            : StatusCode(StatusCodes.Status201Created, result.Value);
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
    public async Task<IActionResult> CheckOut(Guid id, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Error.Unauthorized("auth.required", "Authentication is required to access this resource.").ToActionResult();

        var result = await bookings.CheckOutAsync(id, userId.Value, cancellationToken);
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