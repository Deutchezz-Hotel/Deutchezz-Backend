using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class BookingsController : ControllerBase
{
    [HttpPost]
    public IActionResult CreateBooking()
    {
        return Ok(new { message = "Create booking - to be implemented" });
    }

    [HttpPost("lookup")]
    public IActionResult LookupBooking()
    {
        return Ok(new { message = "Lookup booking - to be implemented" });
    }
}