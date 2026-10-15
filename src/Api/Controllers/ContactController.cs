using Deutchezz.Api.Extensions;
using Deutchezz.Application.Contact;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Deutchezz.Api.Controllers;

/// <summary>
/// Public contact form endpoint. Validates a visitor's message and emails it to the hotel's
/// notification inbox with the visitor set as Reply-To.
/// </summary>
[ApiController]
[Route("api/v1/contact")]
[AllowAnonymous]
public class ContactController(IContactService contact) : ControllerBase
{
    [EnableRateLimiting("contact")]
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] ContactRequest request, CancellationToken cancellationToken)
    {
        var result = await contact.SendAsync(request, cancellationToken);
        return result.ToActionResult();
    }
}
