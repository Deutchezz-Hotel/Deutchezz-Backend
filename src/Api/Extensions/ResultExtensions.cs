using System.Security.Claims;
using Deutchezz.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Api.Extensions;

public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
        {
            return new OkObjectResult(result.Value);
        }

        return result.Error.Type switch
        {
            ErrorType.NotFound => new NotFoundObjectResult(result.Error),
            ErrorType.Validation => new BadRequestObjectResult(result.Error),
            ErrorType.Conflict => new ConflictObjectResult(result.Error),
            ErrorType.Unauthorized => new UnauthorizedObjectResult(result.Error),
            ErrorType.Forbidden => new ObjectResult(result.Error) { StatusCode = StatusCodes.Status403Forbidden },
            ErrorType.Concurrency => new ConflictObjectResult(result.Error),
            _ => new ObjectResult(result.Error) { StatusCode = StatusCodes.Status500InternalServerError }
        };
    }

    public static IActionResult ToActionResult(this Result result)
    {
        if (result.IsSuccess)
        {
            return new NoContentResult();
        }

        return result.Error.Type switch
        {
            ErrorType.NotFound => new NotFoundObjectResult(result.Error),
            ErrorType.Validation => new BadRequestObjectResult(result.Error),
            ErrorType.Conflict => new ConflictObjectResult(result.Error),
            ErrorType.Unauthorized => new UnauthorizedObjectResult(result.Error),
            ErrorType.Forbidden => new ObjectResult(result.Error) { StatusCode = StatusCodes.Status403Forbidden },
            ErrorType.Concurrency => new ConflictObjectResult(result.Error),
            _ => new ObjectResult(result.Error) { StatusCode = StatusCodes.Status500InternalServerError }
        };
    }

    public static Guid? GetUserId(this ControllerBase controller)
    {
        var id = controller.User.FindFirst("sub")?.Value ?? controller.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(id, out var guid) ? guid : null;
    }

    public static List<string> GetRoles(this ControllerBase controller)
    {
        return controller.User.FindAll("role").Select(c => c.Value).ToList();
    }
}