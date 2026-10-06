using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class RoomTypesController : ControllerBase
{
    [HttpGet]
    public IActionResult GetRoomTypes()
    {
        return Ok(new { message = "Get room types - to be implemented" });
    }

    [HttpGet("{id}")]
    public IActionResult GetRoomType(Guid id)
    {
        return Ok(new { message = $"Get room type {id} - to be implemented" });
    }
}