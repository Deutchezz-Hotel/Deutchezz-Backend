using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AvailabilityController : ControllerBase
{
    [HttpPost("search")]
    public IActionResult SearchAvailability()
    {
        return Ok(new { message = "Search availability - to be implemented" });
    }
}