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

}
