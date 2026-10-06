using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
public class DashboardController : ControllerBase
{
    [HttpGet("dashboard")]
    public IActionResult GetDashboard()
    {
        return Ok(new { message = "Dashboard - to be implemented" });
    }
}