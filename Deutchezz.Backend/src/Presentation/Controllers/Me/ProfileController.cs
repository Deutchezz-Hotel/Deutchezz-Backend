using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers.Me;

[ApiController]
[Route("api/v1/me")]
public class ProfileController : ControllerBase
{
    [HttpGet("profile")]
    public IActionResult GetProfile()
    {
        return Ok(new { message = "Get profile - to be implemented" });
    }

    [HttpPut("profile")]
    public IActionResult UpdateProfile()
    {
        return Ok(new { message = "Update profile - to be implemented" });
    }
}