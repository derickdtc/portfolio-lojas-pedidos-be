namespace backend.Exceptions;

public sealed class ApiException(int statusCode, object? responseBody = null) : Exception
{
    public int StatusCode { get; } = statusCode;

    public object? ResponseBody { get; } = responseBody;

    public static ApiException BadRequest(string message) =>
        new(StatusCodes.Status400BadRequest, new { message });

    public static ApiException Unauthorized(string? message = null) =>
        new(StatusCodes.Status401Unauthorized, message is null ? null : new { message });

    public static ApiException Forbidden(string message) =>
        new(StatusCodes.Status403Forbidden, new { message });

    public static ApiException NotFound(string? message = null) =>
        new(StatusCodes.Status404NotFound, message is null ? null : new { message });

    public static ApiException Conflict(string message) =>
        new(StatusCodes.Status409Conflict, new { message });
}
