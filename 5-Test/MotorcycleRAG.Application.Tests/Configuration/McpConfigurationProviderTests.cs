using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Configuration
{
    /// <summary>
    /// Unit tests for McpConfigurationProvider
    /// Tests config provider refresh behavior and tool retrieval
    /// </summary>
    public class McpConfigurationProviderTests : IDisposable
    {
        private readonly Mock<ILogger<McpConfigurationProvider>> _mockLogger;
        private readonly Mock<IToolConfigurationRepository> _mockRepository;
        private readonly McpConfigurationProvider _provider;

        public McpConfigurationProviderTests()
        {
            _mockLogger = new Mock<ILogger<McpConfigurationProvider>>();
            _mockRepository = new Mock<IToolConfigurationRepository>();
            _provider = new McpConfigurationProvider(_mockRepository.Object, _mockLogger.Object);
        }

        #region GetEnabledToolsAsync Tests

        [Fact]
        public async Task GetEnabledToolsAsync_WithEnabledTools_ReturnsOnlyEnabledTools()
        {
            // Arrange
            var enabledTool = BuildConfig(
                toolId: "search-tool",
                name: "Search Tool",
                serverUrl: new Uri("http://localhost:8000"),
                toolType: "search",
                isEnabled: true);

            _mockRepository.Setup(r => r.GetEnabledAsync())
                .ReturnsAsync(new[] { enabledTool });

            // Act
            var result = await _provider.GetEnabledToolsAsync();

            // Assert
            Assert.Single(result);
            Assert.Equal("search-tool", result[0].ToolId);
            Assert.True(result[0].IsEnabled);
        }

        [Fact]
        public async Task GetEnabledToolsAsync_NoEnabledTools_ReturnsEmptyArray()
        {
            // Arrange
            _mockRepository.Setup(r => r.GetEnabledAsync())
                .ReturnsAsync(Array.Empty<McpToolConfiguration>());

            // Act
            var result = await _provider.GetEnabledToolsAsync();

            // Assert
            Assert.Empty(result);
        }

        #endregion

        #region GetToolConfigurationAsync Tests

        [Fact]
        public async Task GetToolConfigurationAsync_ToolExists_ReturnsConfiguration()
        {
            // Arrange
            var toolConfig = BuildConfig(
                toolId: "test-tool",
                name: "Test Tool",
                serverUrl: new Uri("http://localhost:8000"),
                toolType: "search",
                isEnabled: true);

            _mockRepository.Setup(r => r.GetByToolIdAsync("test-tool"))
                .ReturnsAsync(toolConfig);

            // Act
            var result = await _provider.GetToolConfigurationAsync("test-tool");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test-tool", result.ToolId);
            Assert.Equal("Test Tool", result.Name);
        }

        [Fact]
        public async Task GetToolConfigurationAsync_DisabledTool_ReturnsNull()
        {
            // Arrange
            var toolConfig = BuildConfig(
                toolId: "disabled-tool",
                name: "Disabled Tool",
                serverUrl: new Uri("http://localhost:8000"),
                toolType: "search",
                isEnabled: false,
                disabledReason: "Disabled for test");

            _mockRepository.Setup(r => r.GetByToolIdAsync("disabled-tool"))
                .ReturnsAsync(toolConfig);

            // Act
            var result = await _provider.GetToolConfigurationAsync("disabled-tool");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetToolConfigurationAsync_ToolNotExists_ReturnsNull()
        {
            // Arrange
            _mockRepository.Setup(r => r.GetByToolIdAsync("nonexistent-tool"))
                .ReturnsAsync((McpToolConfiguration?)null);

            // Act
            var result = await _provider.GetToolConfigurationAsync("nonexistent-tool");

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region GetToolsByTypeAsync Tests

        [Fact]
        public async Task GetToolsByTypeAsync_WithMatchingTools_ReturnsFilteredTools()
        {
            // Arrange
            var searchTool = BuildConfig(
                toolId: "search-tool-1",
                name: "Search Tool 1",
                serverUrl: new Uri("http://localhost:8000"),
                toolType: "search",
                isEnabled: true,
                priority: 1);

            var searchTool2 = BuildConfig(
                toolId: "search-tool-2",
                name: "Search Tool 2",
                serverUrl: new Uri("http://localhost:8001"),
                toolType: "search",
                isEnabled: true,
                priority: 2);

            _mockRepository.Setup(r => r.GetByTypeAsync("search"))
                .ReturnsAsync(new[] { searchTool, searchTool2 });

            // Act
            var result = await _provider.GetToolsByTypeAsync("search");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.All(result, t => Assert.Equal("search", t.ToolType));
            // Verify priority ordering
            Assert.Equal(1, result[0].Priority);
            Assert.Equal(2, result[1].Priority);
        }

        #endregion

        #region RefreshAsync Tests

        [Fact]
        public async Task RefreshAsync_Multiple_UpdatesLastRefreshTime()
        {
            // Arrange
            var initialTime = _provider.LastRefreshed;

            // Act
            await Task.Delay(10); // Small delay to ensure time difference
            await _provider.RefreshAsync();
            var afterFirstRefresh = _provider.LastRefreshed;

            await Task.Delay(10);
            await _provider.RefreshAsync();
            var afterSecondRefresh = _provider.LastRefreshed;

            // Assert
            Assert.True(afterFirstRefresh > initialTime);
            Assert.True(afterSecondRefresh >= afterFirstRefresh);
        }

        [Fact]
        public async Task RefreshAsync_MultipleCalls_ThreadSafe()
        {
            // Arrange
            var tasks = new Task[10];
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        await _provider.RefreshAsync();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Empty(exceptions);
        }

        #endregion

        #region ValidateToolAsync Tests

        [Fact]
        public async Task ValidateToolAsync_ValidConfiguration_ReturnsTrue()
        {
            // Arrange
            var toolConfig = BuildConfig(
                toolId: "test-tool",
                name: "Test Tool",
                serverUrl: new Uri("http://localhost:9003"),
                toolType: "search",
                isEnabled: true,
                timeoutMs: 30000);

            // Act
            var result = await _provider.ValidateToolAsync(toolConfig);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task ValidateToolAsync_DisabledTool_ReturnsFalse()
        {
            // Arrange
            var toolConfig = BuildConfig(
                toolId: "disabled-tool",
                name: "Disabled Tool",
                serverUrl: new Uri("http://localhost:8000"),
                toolType: "search",
                isEnabled: false,
                disabledReason: "Disabled");

            // Act
            var result = await _provider.ValidateToolAsync(toolConfig);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateToolAsync_NoServerUrl_ReturnsFalse()
        {
            // Arrange
            var toolConfig = BuildConfig(
                toolId: "test-tool",
                name: "Test Tool",
                serverUrl: null,
                toolType: "search",
                isEnabled: true);

            // Act
            var result = await _provider.ValidateToolAsync(toolConfig);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateToolAsync_InvalidServerUrl_ReturnsFalse()
        {
            // Arrange
            var toolConfig = BuildConfig(
                toolId: "test-tool",
                name: "Test Tool",
                serverUrl: new Uri("not-a-valid-url", UriKind.RelativeOrAbsolute),
                toolType: "search",
                isEnabled: true);

            // Act
            var result = await _provider.ValidateToolAsync(toolConfig);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateToolAsync_NullConfiguration_ReturnsFalse()
        {
            // Act
            var result = await _provider.ValidateToolAsync(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateToolAsync_InvalidTimeout_ReturnsFalse()
        {
            // Arrange
            var toolConfig = BuildConfig(
                toolId: "test-tool",
                name: "Test Tool",
                serverUrl: new Uri("http://localhost:8000"),
                toolType: "search",
                isEnabled: true,
                timeoutMs: 0);

            // Act
            var result = await _provider.ValidateToolAsync(toolConfig);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region Configuration Store Integration Tests

        [Fact]
        public async Task GetEnabledToolsAsync_AfterConfigurationChange_ReflectsUpdate()
        {
            // Arrange
            var tool = BuildConfig(
                toolId: "test-tool",
                name: "Test Tool",
                serverUrl: new Uri("http://localhost:8000"),
                toolType: "search",
                isEnabled: true);

            // Act - Get enabled tools before disabling
            _mockRepository.Setup(r => r.GetEnabledAsync())
                .ReturnsAsync(new[] { tool });
            var resultBefore = await _provider.GetEnabledToolsAsync();
            Assert.Single(resultBefore);

            // Disable the tool and update mock
            tool.Disable("Testing");
            _mockRepository.Setup(r => r.GetEnabledAsync())
                .ReturnsAsync(Array.Empty<McpToolConfiguration>());

            // Get enabled tools after disabling
            var resultAfter = await _provider.GetEnabledToolsAsync();

            // Assert
            Assert.Empty(resultAfter);
        }

        #endregion

        /// <summary>
        /// Constructs a fully-formed <see cref="McpToolConfiguration"/> for tests,
        /// routing through <see cref="McpToolConfiguration.Rehydrate"/> so transition
        /// state (e.g. IsEnabled=false, TimeoutMs=0, null ServerUrl) can be expressed
        /// without relying on illegal setter access on the entity.
        /// </summary>
        private static McpToolConfiguration BuildConfig(
            string toolId,
            string name,
            Uri? serverUrl,
            string toolType,
            bool isEnabled = true,
            int priority = 0,
            int? timeoutMs = 30000,
            string? disabledReason = null) =>
            McpToolConfiguration.Rehydrate(
                id: Guid.NewGuid(),
                toolId: toolId,
                name: name,
                description: null,
                serverUrl: serverUrl,
                toolType: toolType,
                version: null,
                isSystemTool: false,
                priority: priority,
                timeoutMs: timeoutMs,
                retryOnFailure: true,
                maxRetries: 3,
                createdAt: DateTime.UtcNow,
                isEnabled: isEnabled,
                configurationJson: null,
                disabledReason: disabledReason,
                lastTestedAt: null,
                lastConnectionStatus: null,
                updatedAt: null);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _provider?.Dispose();
            }
        }
    }
}
