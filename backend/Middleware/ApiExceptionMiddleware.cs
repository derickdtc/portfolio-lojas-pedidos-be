using backend.Exceptions;

namespace backend.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException exception)
        {
            await WriteResponseAsync(context, exception.StatusCode, exception.ResponseBody);
        }
        catch (UnauthorizedAccessException exception)
        {
            await WriteResponseAsync(
                context,
                StatusCodes.Status401Unauthorized,
                new { message = exception.Message });
        }
    }

    private static async Task WriteResponseAsync(HttpContext context, int statusCode, object? responseBody)
    {
        context.Response.StatusCode = statusCode;

        if (responseBody is not null)
        {
            await context.Response.WriteAsJsonAsync(responseBody, context.RequestAborted);
        }
    }
}
