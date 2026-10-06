using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers.Reception;

[ApiController]
[Route("api/v1/reception")]
public class BookingsController : ControllerBase
{
    [HttpGet("bookings")]
    public IActionResult GetBookings()
    {
        return Ok(new { message = "Get bookings - to be implemented" });
    }

    [HttpGet("bookings/{id}")]
    public IActionResult GetBooking(Guid id)
    {
        return Ok(new { message = $"Get booking {id} - to be implemented" });
    }

    [HttpPost("bookings/{id}/assign-rooms")]
    public IActionResult AssignRooms(Guid id)
    {
        return Ok(new { message = $"Assign rooms {id} - to be implemented" });
    }

    [HttpPost("bookings/{id}/check-in")]
    public IActionResult CheckIn(Guid id)
    {
        return Ok(new { message = $"Check in {id} - to be implemented" });
    }

    [HttpPost("bookings/{id}/check-out")]
    public IActionResult CheckOut(Guid id)
    {
        return Ok(new { message = $"Check out {id} - to be implemented" });
    }
}