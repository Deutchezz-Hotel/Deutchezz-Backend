using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Backend.Presentation.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
public class RoomsController : ControllerBase
{
    [HttpGet("rooms")]
    public IActionResult GetRooms()
    {
        return Ok(new { message = "Get rooms - to be implemented" });
    }

    [HttpPost("rooms")]
    public IActionResult CreateRoom()
    {
        return Ok(new { message = "Create room - to be implemented" });
    }

    [HttpGet("rooms/{id}")]
    public IActionResult GetRoom(Guid id)
    {
        return Ok(new { message = $"Get room {id} - to be implemented" });
    }

    [HttpPut("rooms/{id}")]
    public IActionResult UpdateRoom(Guid id)
    {
        return Ok(new { message = $"Update room {id} - to be implemented" });
    }

    [HttpPatch("rooms/{id}/status")]
    public IActionResult UpdateRoomStatus(Guid id)
    {
        return Ok(new { message = $"Update room status {id} - to be implemented" });
    }
}