using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace MotorcycleRAG.API.Tests.Api;

/// <summary>
/// Shared helpers for API unit tests (DefaultHttpContext setup, response-body capture, configuration).
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Creates a <see cref="DefaultHttpContext"/> with a memory-backed response body that can be read after invocation.
    /// </summary>
    internal static DefaultHttpContext CreateContext(string path = "/", string method = "GET")
    {
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Host = new HostString("localhost");
        context.Request.Headers["Host"] = "localhost";
        return context;
    }

    /// <summary>
    /// Reads the captured response body as a string.
    /// </summary>
    internal static string ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Builds an <see cref="IConfiguration"/> from an in-memory key/value dictionary.
    /// </summary>
    internal static IConfiguration BuildConfig(params KeyValuePair<string, string?>[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    /// <summary>
    /// Builds an <see cref="IConfiguration"/> from a dictionary.
    /// </summary>
    internal static IConfiguration BuildConfig(IDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values!)
            .Build();
    }

    /// <summary>
    /// A <see cref="RequestDelegate"/> that does nothing (terminal middleware stub).
    /// </summary>
    internal static RequestDelegate NoopNext => _ => Task.CompletedTask;

    /// <summary>
    /// A <see cref="RequestDelegate"/> that sets a marker status code so callers can assert the pipeline continued.
    /// </summary>
    internal static RequestDelegate ContinueNext => ctx =>
    {
        ctx.Response.StatusCode = 299;
        return Task.CompletedTask;
    };
}
