using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    [HttpPost("register")]
    public IActionResult Register()
    {
        return Ok(new { message = "Register endpoint - to be implemented" });
    }

    [HttpPost("login")]
    public IActionResult Login()
    {
        return Ok(new { message = "Login endpoint - to be implemented" });
    }

    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        return Ok(new { message = "Current user - to be implemented" });
    }
}