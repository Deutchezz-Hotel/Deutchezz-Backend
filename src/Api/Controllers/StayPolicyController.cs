using Deutchezz.Application.Stays;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Api.Controllers;

/// <summary>
/// Public, cacheable stay-policy endpoint (v1.12). Tells room-timers (booking forms, availability)
/// which check-in/check-out times are accepted and how stays may be stretched.
/// </summary>
[ApiController]
[Route("api/v1")]
[AllowAnonymous]
public class StayPolicyController(IStayPolicyService stays) : ControllerBase
{
    [HttpGet("stay-policy")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var policy = await stays.GetPolicyAsync(cancellationToken);

        return Ok(new StayPolicyResponse(
            Format(policy.DefaultCheckInTime),
            Format(policy.DefaultCheckOutTime),
            policy.TimeSlotMinutes,
            Format(policy.EarliestCheckInTime),
            Format(policy.LatestCheckInTime),
            Format(policy.EarliestCheckOutTime),
            Format(policy.LatestCheckOutTime),
            policy.TurnaroundMinutes,
            policy.MaxStayNights,
            policy.GraceHours));
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm");
}

public sealed record StayPolicyResponse(
    string DefaultCheckInTime,
    string DefaultCheckOutTime,
    int TimeSlotMinutes,
    string EarliestCheckInTime,
    string LatestCheckInTime,
    string EarliestCheckOutTime,
    string LatestCheckOutTime,
    int TurnaroundMinutes,
    int MaxStayNights,
    int GraceHours);