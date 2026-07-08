using System.Net;
using System.Reflection;
using System.Text.Json;

namespace CrediPrest.Api;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            var actualException = exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException
                : exception;

            logger.LogError(actualException, "Unhandled API exception while processing {Method} {Path}.", context.Request.Method, context.Request.Path);

            var statusCode = actualException switch
            {
                UnauthorizedAccessException => HttpStatusCode.Unauthorized,
                KeyNotFoundException => HttpStatusCode.NotFound,
                InvalidOperationException or ArgumentException => HttpStatusCode.BadRequest,
                _ => HttpStatusCode.InternalServerError
            };

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = actualException.Message,
                statusCode = context.Response.StatusCode
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
