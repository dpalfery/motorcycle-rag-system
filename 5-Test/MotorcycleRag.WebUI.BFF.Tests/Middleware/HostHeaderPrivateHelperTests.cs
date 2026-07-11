using System.Reflection;
using MotorcycleRag.WebUI.BFF.Middleware;

namespace MotorcycleRag.WebUI.BFF.Tests.Middleware;

public class HostHeaderPrivateHelperTests {
    [Fact]
    public void ExtractHostname_WithWhitespace_ReturnsEmpty() {
        var method = typeof(HostHeaderValidationMiddleware).GetMethod(
            "ExtractHostname",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, ["   "]);

        result.Should().Be(string.Empty);
    }

    [Fact]
    public void SanitizeLogValue_WithNull_ReturnsEmpty() {
        var method = typeof(HostHeaderValidationMiddleware).GetMethod(
            "SanitizeLogValue",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, [null, 200]);

        result.Should().Be(string.Empty);
    }

    [Fact]
    public void SanitizeLogValue_WithLongValue_Truncates() {
        var method = typeof(HostHeaderValidationMiddleware).GetMethod(
            "SanitizeLogValue",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var longValue = new string('a', 250);

        var result = method!.Invoke(null, [longValue, 200]) as string;

        result.Should().HaveLength(200);
    }
}
