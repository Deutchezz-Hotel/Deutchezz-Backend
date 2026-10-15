using Deutchezz.Api.Extensions;
using Deutchezz.Application.Stays;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Api.Controllers;

/// <summary>
/// Public pricing quotes (v1.14). The single source of the duration-based billing formula, so the
/// booking forms never duplicate it client-side.
/// </summary>
[ApiController]
[Route("api/v1/pricing")]
[AllowAnonymous]
public class PricingController(IPricingService pricing) : ControllerBase
{
    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] PricingQuoteRequest request, CancellationToken cancellationToken)
    {
        var result = await pricing.QuoteAsync(request, cancellationToken);
        return result.ToActionResult();
    }
}
