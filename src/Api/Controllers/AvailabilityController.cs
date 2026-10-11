using Deutchezz.Application.Availability;
using Deutchezz.Api.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Deutchezz.Api.Controllers;

[ApiController]
[Route("api/v1/availability")]
public class AvailabilityController(IAvailabilityService service) : ControllerBase
{
    [EnableRateLimiting("lookup")]
    [HttpPost("search")]
    public async Task<IActionResult> Search(AvailabilitySearchRequest request, CancellationToken cancellationToken)
    {
        var result = await service.SearchAsync(request, cancellationToken);
        return result.ToActionResult();
    }
}