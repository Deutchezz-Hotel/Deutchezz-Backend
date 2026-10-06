using Deutchezz.Application.RoomTypes;
using Deutchezz.Api.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class RoomTypesController(IRoomTypesService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Deutchezz.Application.RoomTypes.RoomTypeQuery query, CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }
}