using System.Text.Json;
using NetTranslate.Domain.Exceptions;

namespace NetTranslate.Api.Common;

/// <summary>Chuyển exception thành JSON {"error": "..."} với status code giống LibreTranslate.</summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // client huỷ request — không cần log lỗi
        }
        catch (Exception ex)
        {
            var (status, message) = ex switch
            {
                ValidationException e => (StatusCodes.Status400BadRequest, e.Message),
                ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
                NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
                RateLimitExceededException e => (StatusCodes.Status429TooManyRequests, e.Message),
                BadHttpRequestException e => (e.StatusCode, e.Message),
                JsonException => (StatusCodes.Status400BadRequest, "Invalid request: malformed JSON"),
                _ => (StatusCodes.Status500InternalServerError, "Cannot translate text: internal server error"),
            };

            if (status >= 500) logger.LogError(ex, "Unhandled error on {Path}", context.Request.Path);
            else logger.LogDebug("Request error {Status}: {Message}", status, message);

            if (context.Response.HasStarted) throw;

            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { error = message });
        }
    }
}
