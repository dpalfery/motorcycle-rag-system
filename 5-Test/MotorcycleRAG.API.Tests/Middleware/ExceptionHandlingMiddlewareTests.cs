using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Middleware;

namespace MotorcycleRAG.UnitTests.Presentation.API.Middleware;

public sealed class ExceptionHandlingMiddlewareTests
{
    public static IEnumerable<object[]> HandledExceptions()
    {
        yield return [new ArgumentNullException("uploadId"), StatusCodes.Status400BadRequest, "Missing required parameter", "A required parameter is missing", "uploadId"];
        yield return [new ArgumentException("invalid", "documentType"), StatusCodes.Status400BadRequest, "Invalid argument", "One or more arguments are invalid. Please check your input", "documentType"];
        yield return [new UnauthorizedAccessException(), StatusCodes.Status401Unauthorized, "Unauthorized access", "You are not authorized to access this resource", null!];
        yield return [new InvalidOperationException(), StatusCodes.Status400BadRequest, "Invalid operation", "The requested operation is invalid in this context", null!];
        yield return [new TimeoutException(), StatusCodes.Status408RequestTimeout, "Request timeout", "The request took too long to complete", null!];
        yield return [new NotImplementedException(), StatusCodes.Status501NotImplemented, "Not implemented", "This functionality is not yet implemented", null!];
        yield return [new NotSupportedException(), StatusCodes.Status400BadRequest, "Not supported", "This operation is not supported", null!];
        yield return [new Exception("internal detail"), StatusCodes.Status500InternalServerError, "Internal server error", "Contact support with Reference ID: trace-123", null!];
    }

    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        ((Action)(() => new ExceptionHandlingMiddleware(null!, NullLogger<ExceptionHandlingMiddleware>.Instance)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new ExceptionHandlingMiddleware(next, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_WithNullContext_ThrowsArgumentNullException()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        var act = () => middleware.InvokeAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_WhenNextSucceeds_LeavesResponseUntouched()
    {
        var nextInvocations = 0;
        var middleware = CreateMiddleware(_ =>
        {
            nextInvocations++;
            return Task.CompletedTask;
        });
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        nextInvocations.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Length.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(HandledExceptions))]
    public async Task InvokeAsync_WhenNextThrows_ReturnsSafeProblemDetails(
        Exception exception,
        int expectedStatus,
        string expectedTitle,
        string expectedDetail,
        string? expectedParameter)
    {
        var middleware = CreateMiddleware(_ => Task.FromException(exception));
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(expectedStatus);
        context.Response.ContentType.Should().Be("application/problem+json");
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        response.RootElement.GetProperty("title").GetString().Should().Be(expectedTitle);
        response.RootElement.GetProperty("status").GetInt32().Should().Be(expectedStatus);
        response.RootElement.GetProperty("detail").GetString().Should().Contain(expectedDetail);
        response.RootElement.GetProperty("referenceId").GetString().Should().Be("trace-123");
        if (expectedParameter is null)
        {
            response.RootElement.TryGetProperty("parameter", out _).Should().BeFalse();
        }
        else
        {
            response.RootElement.GetProperty("parameter").GetString().Should().Be(expectedParameter);
        }
    }

    [Fact]
    public void UseExceptionHandling_AddsMiddlewareToApplicationBuilder()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);

        var returnedBuilder = ExceptionHandlingMiddlewareExtensions.UseExceptionHandling(builder);

        returnedBuilder.Should().BeSameAs(builder);
    }

    private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-123";
        context.Request.Path = "/api/query";
        context.Response.Body = new MemoryStream();
        return context;
    }
}
