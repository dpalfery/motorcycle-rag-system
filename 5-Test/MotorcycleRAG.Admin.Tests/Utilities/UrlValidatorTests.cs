using FluentAssertions;
using MotorcycleRAG.Admin.Utilities;

namespace MotorcycleRAG.Admin.Tests.Utilities;

public class UrlValidatorTests
{
    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://localhost:8100")]
    [InlineData("http://127.0.0.1:8008")]
    [InlineData("http://[::1]:7215")]
    public void IsValidUrl_AllowsLocalDevelopmentEndpoints_WhenEnabled(string url)
    {
        var isValid = UrlValidator.IsValidUrl(url, allowLocalhost: true);

        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://[::1]:7215")]
    public void IsValidUrl_BlocksLocalDevelopmentEndpoints_WhenLocalhostDisabled(string url)
    {
        var isValid = UrlValidator.IsValidUrl(url, allowLocalhost: false);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void IsValidUrl_BlocksDevelopmentPortsForNonLocalHosts()
    {
        var isValid = UrlValidator.IsValidUrl("http://example.com:8008", allowLocalhost: true);

        isValid.Should().BeFalse();
    }
}
