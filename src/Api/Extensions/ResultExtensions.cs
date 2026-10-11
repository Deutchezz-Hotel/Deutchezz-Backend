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

        return result.Error.ToActionResult();
    }

    public static IActionResult ToActionResult(this Result result)
    {
        if (result.IsSuccess)
        {
            return new NoContentResult();
        }

        return result.Error.ToActionResult();
    }

    public static IActionResult ToActionResult(this Error error)
    {
        var status = StatusCodeFor(error.Type);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(error.Type),
            Detail = error.Description,
            Type = $"https://httpstatuses.com/{status}"
        };
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Concurrency => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed.",
        ErrorType.Unauthorized => "Unauthorized.",
        ErrorType.Forbidden => "Forbidden.",
        ErrorType.NotFound => "Resource not found.",
        ErrorType.Conflict => "Conflict.",
        ErrorType.Concurrency => "Concurrency conflict.",
        _ => "An unexpected error occurred."
    };

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