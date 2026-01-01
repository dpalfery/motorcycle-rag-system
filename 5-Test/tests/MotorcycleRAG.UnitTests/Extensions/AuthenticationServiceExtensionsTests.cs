using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.API.Extensions;
using Xunit;

namespace MotorcycleRAG.UnitTests.Extensions;

/// <summary>
/// Unit tests for AuthenticationServiceExtensions
/// Tests dual-issuer JWT bearer authentication configuration
/// </summary>
public class AuthenticationServiceExtensionsTests
{
    private IConfiguration CreateConfiguration(
        string workforceIssuer,
        string? externalIdIssuer = null,
        string audience = "api://motorcyclerag")
    {
        var config = new Dictionary<string, string>
        {
            ["Authentication:Issuers:Workforce"] = workforceIssuer,
            ["Authentication:Audience"] = audience
        };

        if (!string.IsNullOrEmpty(externalIdIssuer))
        {
            config["Authentication:Issuers:ExternalId"] = externalIdIssuer;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();
    }

    [Fact]
    public void MissingWorkforceIssuer_ShouldThrowException()
    {
        // Arrange
        var config = CreateConfiguration("");
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<AuthenticationServiceExtensions>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            var authBuilder = services.AddAuthentication();
            authBuilder.AddDualIssuerJwtBearer(config, logger);
        });
    }

    [Fact]
    public void MissingAudience_ShouldThrowException()
    {
        // Arrange
        var configDict = new Dictionary<string, string>
        {
            ["Authentication:Issuers:Workforce"] = "https://login.microsoftonline.com/tenant-id/v2.0",
            ["Authentication:Audience"] = ""
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<AuthenticationServiceExtensions>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            var authBuilder = services.AddAuthentication();
            authBuilder.AddDualIssuerJwtBearer(config, logger);
        });
    }

    [Fact]
    public void ValidWorkforceIssuerOnly_ShouldSucceed()
    {
        // Arrange
        var config = CreateConfiguration("https://login.microsoftonline.com/tenant-id/v2.0");
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<AuthenticationServiceExtensions>();

        // Act
        var authBuilder = services.AddAuthentication();
        authBuilder.AddDualIssuerJwtBearer(config, logger);

        // Assert - Should not throw and should register JWT bearer scheme
        var provider = services.BuildServiceProvider();
        var authService = provider.GetRequiredService<IAuthenticationService>();
        Assert.NotNull(authService);
    }

    [Fact]
    public void ValidWorkforceAndExternalIdIssuers_ShouldSucceed()
    {
        // Arrange
        var config = CreateConfiguration(
            "https://login.microsoftonline.com/tenant-id/v2.0",
            "https://tenant.b2clogin.com/tenant.onmicrosoft.com/b2c_1_susi/v2.0"
        );
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<AuthenticationServiceExtensions>();

        // Act
        var authBuilder = services.AddAuthentication();
        authBuilder.AddDualIssuerJwtBearer(config, logger);

        // Assert
        var provider = services.BuildServiceProvider();
        var authService = provider.GetRequiredService<IAuthenticationService>();
        Assert.NotNull(authService);
    }

    [Fact]
    public void ConfigurationValidation_ShouldLogWarningForMissingExternalId()
    {
        // Arrange
        var config = CreateConfiguration("https://login.microsoftonline.com/tenant-id/v2.0");
        var services = new ServiceCollection();

        // Create logger that captures warnings
        var logMessages = new List<string>();
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestLoggerProvider(logMessages));
        });
        var logger = loggerFactory.CreateLogger<AuthenticationServiceExtensions>();

        // Act
        var authBuilder = services.AddAuthentication();
        authBuilder.AddDualIssuerJwtBearer(config, logger);

        // Assert - Should have logged a warning about missing External ID
        Assert.Contains(logMessages, msg =>
            msg.Contains("External ID issuer is not configured") ||
            msg.Contains("Configuring dual-issuer JWT authentication")
        );
    }

    [Fact]
    public void ConfigurationValidation_ShouldLogInfoForConfiguredExternalId()
    {
        // Arrange
        var config = CreateConfiguration(
            "https://login.microsoftonline.com/tenant-id/v2.0",
            "https://tenant.b2clogin.com/tenant.onmicrosoft.com/b2c_1_susi/v2.0"
        );
        var services = new ServiceCollection();

        var logMessages = new List<string>();
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestLoggerProvider(logMessages));
        });
        var logger = loggerFactory.CreateLogger<AuthenticationServiceExtensions>();

        // Act
        var authBuilder = services.AddAuthentication();
        authBuilder.AddDualIssuerJwtBearer(config, logger);

        // Assert
        Assert.Contains(logMessages, msg =>
            msg.Contains("External ID issuer configured") ||
            msg.Contains("Configuring dual-issuer JWT authentication")
        );
    }

    [Fact]
    public void JwtBearerScheme_ShouldBeRegistered()
    {
        // Arrange
        var config = CreateConfiguration("https://login.microsoftonline.com/tenant-id/v2.0");
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<AuthenticationServiceExtensions>();

        // Act
        var authBuilder = services.AddAuthentication();
        authBuilder.AddDualIssuerJwtBearer(config, logger);

        // Assert
        var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var jwtScheme = schemes.GetSchemeAsync("Bearer").Result;
        Assert.NotNull(jwtScheme);
        Assert.Equal("Bearer", jwtScheme.Name);
    }
}

/// <summary>
/// Test logger provider for capturing log messages
/// </summary>
internal class TestLoggerProvider : ILoggerProvider
{
    private readonly List<string> _logMessages;

    public TestLoggerProvider(List<string> logMessages)
    {
        _logMessages = logMessages;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestLogger(_logMessages);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Test logger for capturing log messages
/// </summary>
internal class TestLogger : ILogger
{
    private readonly List<string> _logMessages;

    public TestLogger(List<string> logMessages)
    {
        _logMessages = logMessages;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logMessages.Add(formatter(state, exception));
    }
}
