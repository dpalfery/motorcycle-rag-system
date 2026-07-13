using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.API.Services;

namespace MotorcycleRAG.UnitTests.Presentation.API.Services;

public sealed class HealthCheckResponseWriterTests
{
    [Fact]
    public async Task WriteResponse_MapsHealthReportToCamelCaseJsonAndAlwaysReturnsOk()
    {
        // Arrange
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["sql"] = new(
                    HealthStatus.Degraded,
                    "Connection pool exhausted",
                    TimeSpan.FromMilliseconds(1250),
                    exception: null,
                    data: new Dictionary<string, object> { ["poolSize"] = 20 }),
                ["search"] = new(
                    HealthStatus.Healthy,
                    description: null,
                    TimeSpan.FromMilliseconds(25),
                    exception: null,
                    data: new Dictionary<string, object>()),
            },
            TimeSpan.FromMilliseconds(1275));

        // Act
        await HealthCheckResponseWriter.WriteResponse(context, report);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.ContentType.Should().Be("application/json; charset=utf-8");
        json.RootElement.GetProperty("status").GetString().Should().Be("Degraded");
        json.RootElement.GetProperty("totalDuration").GetString()
            .Should().Be(report.TotalDuration.ToString("G"));
        var sql = json.RootElement.GetProperty("checks").GetProperty("sql");
        sql.GetProperty("status").GetString().Should().Be("Degraded");
        sql.GetProperty("duration").GetString().Should().Be(TimeSpan.FromMilliseconds(1250).ToString("G"));
        sql.GetProperty("description").GetString().Should().Be("Connection pool exhausted");
        sql.GetProperty("data").GetProperty("poolSize").GetInt32().Should().Be(20);
        json.RootElement.GetProperty("checks").GetProperty("search").TryGetProperty("description", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task WriteResponse_WithNullRequiredInput_ThrowsArgumentNullException()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero);

        // Act
        var nullContext = () => HealthCheckResponseWriter.WriteResponse(null!, report);
        var nullReport = () => HealthCheckResponseWriter.WriteResponse(context, null!);

        // Assert
        await nullContext.Should().ThrowAsync<ArgumentNullException>();
        await nullReport.Should().ThrowAsync<ArgumentNullException>();
    }
}
