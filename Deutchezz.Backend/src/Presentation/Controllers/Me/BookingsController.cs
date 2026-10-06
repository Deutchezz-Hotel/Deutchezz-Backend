using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers.Me;

[ApiController]
[Route("api/v1/me")]
public class BookingsController : ControllerBase
{
    [HttpGet("bookings")]
    public IActionResult GetMyBookings()
    {
        return Ok(new { message = "My bookings - to be implemented" });
    }

    [HttpGet("bookings/{id}")]
    public IActionResult GetMyBooking(Guid id)
    {
        return Ok(new { message = $"My booking {id} - to be implemented" });
    }

    [HttpPost("bookings/{id}/cancel")]
    public IActionResult CancelMyBooking(Guid id)
    {
        return Ok(new { message = $"Cancel booking {id} - to be implemented" });
    }
}