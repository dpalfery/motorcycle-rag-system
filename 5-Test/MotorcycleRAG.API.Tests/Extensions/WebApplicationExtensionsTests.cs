using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.API.Extensions;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Extensions;

/// <summary>
/// Covers <see cref="WebApplicationExtensions.PreWarmJwtSigningKeysAsync"/>, which is internal to the
/// API assembly and accessible here via <c>InternalsVisibleTo("MotorcycleRAG.API.Tests")</c>.
/// </summary>
public sealed class WebApplicationExtensionsTests
{
    private const string WorkforceIssuer = "https://issuer.example.com";
    private const string MetadataUrl = "https://issuer.example.com/.well-known/openid-configuration";
    private const string JwksUrl = "https://issuer.example.com/discovery/v2.0/keys";

    private static WebApplicationBuilder CreateBuilder(List<string> capturedLogs, string? workforceIssuer)
    {
        var builder = WebApplication.CreateBuilder();

        if (workforceIssuer is not null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Issuers:Workforce"] = workforceIssuer
            });
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CapturingLoggerProvider(capturedLogs));

        return builder;
    }

    [Fact]
    public async Task PreWarmJwtSigningKeysAsync_WhenWorkforceIssuerConfiguredAndCacheSucceeds_LogsCompletion()
    {
        // Arrange
        var capturedLogs = new List<string>();
        var builder = CreateBuilder(capturedLogs, WorkforceIssuer);

        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.OK, $$"""{"jwks_uri":"{{JwksUrl}}"}""", "application/json");
        handler.SetupRequest(HttpMethod.Get, JwksUrl)
            .ReturnsResponse(HttpStatusCode.OK, """{"keys":[{"kty":"RSA","kid":"key-1","n":"xGOr-H7A-PWG","e":"AQAB"}]}""", "application/json");

        var signingKeyCache = new SigningKeyCache(handler.CreateClient(), NullLogger<SigningKeyCache>.Instance);
        builder.Services.AddSingleton(signingKeyCache);

        await using var app = builder.Build();

        // Act
        await app.PreWarmJwtSigningKeysAsync();

        // Assert
        capturedLogs.Should().Contain(m => m.Contains("Pre-warming JWT signing key cache"));
        capturedLogs.Should().Contain(m => m.Contains("JWT signing key cache pre-warming completed"));
        capturedLogs.Should().NotContain(m => m.Contains("Error pre-warming"));
        signingKeyCache.GetSigningKeys(WorkforceIssuer).Should().ContainSingle();
    }

    [Fact]
    public async Task PreWarmJwtSigningKeysAsync_WhenWorkforceIssuerConfiguredButCacheNotRegistered_LogsErrorAndDoesNotThrow()
    {
        // Arrange — SigningKeyCache is intentionally NOT registered so GetRequiredService throws,
        // exercising the try/catch "exception during pre-warm" branch.
        var capturedLogs = new List<string>();
        var builder = CreateBuilder(capturedLogs, WorkforceIssuer);

        await using var app = builder.Build();

        // Act
        var act = async () => await app.PreWarmJwtSigningKeysAsync();

        // Assert
        await act.Should().NotThrowAsync();
        capturedLogs.Should().Contain(m => m.Contains("Error pre-warming JWT signing key cache"));
        capturedLogs.Should().NotContain(m => m.Contains("JWT signing key cache pre-warming completed"));
    }

    [Fact]
    public async Task PreWarmJwtSigningKeysAsync_WhenWorkforceIssuerNotConfigured_LogsWarningAndSkipsPreWarm()
    {
        // Arrange
        var capturedLogs = new List<string>();
        var builder = CreateBuilder(capturedLogs, workforceIssuer: null);

        await using var app = builder.Build();

        // Act
        await app.PreWarmJwtSigningKeysAsync();

        // Assert
        capturedLogs.Should().Contain(m => m.Contains("Workforce issuer not configured"));
        capturedLogs.Should().NotContain(m => m.Contains("Pre-warming JWT signing key cache"));
        capturedLogs.Should().NotContain(m => m.Contains("Error pre-warming"));
    }

    private sealed class CapturingLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (messages)
            {
                messages.Add($"[{logLevel}] {formatter(state, exception)}");
            }
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
