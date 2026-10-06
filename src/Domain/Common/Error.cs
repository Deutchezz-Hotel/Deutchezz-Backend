namespace Deutchezz.Domain.Common;

public enum ErrorType
{
    NotFound,
    Validation,
    Conflict,
    Unauthorized,
    Forbidden,
    Concurrency,
    Failure
}

public record Error(ErrorType Type, string Code, string? Description = null)
{
    public static Error NotFound(string code = "not.found", string? description = null) =>
        new(ErrorType.NotFound, code, description);

    public static Error Validation(string code = "validation.error", string? description = null) =>
        new(ErrorType.Validation, code, description);

    public static Error Conflict(string code = "conflict", string? description = null) =>
        new(ErrorType.Conflict, code, description);

    public static Error Unauthorized(string code = "unauthorized", string? description = null) =>
        new(ErrorType.Unauthorized, code, description);

    public static Error Forbidden(string code = "forbidden", string? description = null) =>
        new(ErrorType.Forbidden, code, description);

    public static Error Concurrency(string code = "concurrency", string? description = null) =>
        new(ErrorType.Concurrency, code, description);

    public static Error Failure(string code = "failure", string? description = null) =>
        new(ErrorType.Failure, code, description);
}