using Deutchezz.Api.Extensions;
using Deutchezz.Application.Bookings;
using Deutchezz.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Deutchezz.Api.Controllers;

[ApiController]
[Route("api/v1/bookings")]
public class BookingsController(IBookingService bookings) : ControllerBase
{
    [EnableRateLimiting("booking-create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Error.Validation("idempotency_key_required", "The Idempotency-Key header is required").ToActionResult();

        var userId = this.GetUserId();
        var result = await bookings.CreateAsync(request, userId, idempotencyKey, cancellationToken);
        if (result.IsFailure)
            return result.ToActionResult();

        if (result.Value.Replay)
            return Ok(result.Value);

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [EnableRateLimiting("lookup")]
    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup(BookingLookupRequest request, CancellationToken cancellationToken)
    {
        var result = await bookings.LookupAsync(request, cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting("lookup")]
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(AnonymousCancelBookingRequest request, CancellationToken cancellationToken)
    {
        // Anonymous cancellation: the booking reference + email pair authorises the action,
        // exactly like lookup. Registered customers use POST /me/bookings/{id}/cancel instead.
        var lookup = new BookingLookupRequest(request.BookingReference, request.Email);
        var result = await bookings.CancelAsync(null, lookup, request.Reason, null, cancellationToken);
        return result.ToActionResult();
    }
}