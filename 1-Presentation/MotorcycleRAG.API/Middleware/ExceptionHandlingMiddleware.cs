using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace MotorcycleRAG.API.Middleware;

/// <summary>
/// Middleware for handling exceptions and returning ProblemDetails responses
/// </summary>
internal sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the ExceptionHandlingMiddleware
    /// </summary>
    /// <param name="next">Next middleware in the pipeline</param>
    /// <param name="logger">Logger</param>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Invokes the middleware
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <returns>Task</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Handles exceptions and returns appropriate ProblemDetails response
    /// </summary>
    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        var problemDetails = new ProblemDetails
        {
            Title = "An unexpected error occurred",
            Status = (int)HttpStatusCode.InternalServerError,
            Detail = exception.Message,
            Instance = context.Request.Path
        };

        // Add correlation ID if available
        if (!string.IsNullOrEmpty(context.TraceIdentifier))
        {
            problemDetails.Extensions["traceId"] = context.TraceIdentifier;
        }

        // Handle specific exception types
        switch (exception)
        {
            case ArgumentNullException nullEx:
                problemDetails.Title = "Missing required parameter";
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Detail = nullEx.Message;
                problemDetails.Extensions["parameterName"] = nullEx.ParamName;
                break;

            case ArgumentException argEx:
                problemDetails.Title = "Invalid argument";
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Detail = argEx.Message;
                problemDetails.Extensions["parameterName"] = argEx.ParamName;
                break;

            case UnauthorizedAccessException:
                problemDetails.Title = "Unauthorized access";
                problemDetails.Status = (int)HttpStatusCode.Unauthorized;
                problemDetails.Detail = "You are not authorized to access this resource";
                break;

            case InvalidOperationException opEx:
                problemDetails.Title = "Invalid operation";
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Detail = opEx.Message;
                break;

            case TimeoutException:
                problemDetails.Title = "Request timeout";
                problemDetails.Status = (int)HttpStatusCode.RequestTimeout;
                problemDetails.Detail = "The request timed out";
                break;

            case NotImplementedException:
                problemDetails.Title = "Not implemented";
                problemDetails.Status = (int)HttpStatusCode.NotImplemented;
                problemDetails.Detail = "This functionality is not yet implemented";
                break;

            case NotSupportedException:
                problemDetails.Title = "Not supported";
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Detail = "This operation is not supported";
                break;

            default:
                problemDetails.Title = "Internal server error";
                problemDetails.Status = (int)HttpStatusCode.InternalServerError;
                problemDetails.Detail = "An unexpected error occurred";
                break;
        }

        // Set response content type and status code
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = problemDetails.Status ?? (int)HttpStatusCode.InternalServerError;

        // Serialize and write the ProblemDetails response
        var jsonResponse = JsonSerializer.Serialize(problemDetails, JsonOptions);
        await context.Response.WriteAsync(jsonResponse);
    }
}

/// <summary>
/// Extension method for adding the exception handling middleware
/// </summary>
internal static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>
    /// Adds the exception handling middleware to the pipeline
    /// </summary>
    /// <param name="builder">Web application builder</param>
    /// <returns>Web application builder</returns>
    internal static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
