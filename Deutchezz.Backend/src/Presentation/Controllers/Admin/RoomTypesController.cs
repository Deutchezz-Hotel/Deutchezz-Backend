using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
public class RoomTypesController : ControllerBase
{
    [HttpGet("room-types")]
    public IActionResult GetRoomTypes()
    {
        return Ok(new { message = "Get room types - to be implemented" });
    }

    [HttpPost("room-types")]
    public IActionResult CreateRoomType()
    {
        return Ok(new { message = "Create room type - to be implemented" });
    }

    [HttpGet("room-types/{id}")]
    public IActionResult GetRoomType(Guid id)
    {
        return Ok(new { message = $"Get room type {id} - to be implemented" });
    }

    [HttpPut("room-types/{id}")]
    public IActionResult UpdateRoomType(Guid id)
    {
        return Ok(new { message = $"Update room type {id} - to be implemented" });
    }

    [HttpPatch("room-types/{id}/status")]
    public IActionResult UpdateRoomTypeStatus(Guid id)
    {
        return Ok(new { message = $"Update room type status {id} - to be implemented" });
    }
}