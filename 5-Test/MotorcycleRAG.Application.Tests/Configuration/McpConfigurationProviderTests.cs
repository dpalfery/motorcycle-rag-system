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
            var enabledTool = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "search-tool",
                Name = "Search Tool",
                ServerUrl = new Uri("http://localhost:8000"),
                ToolType = "search",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            };

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
            var toolConfig = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "test-tool",
                Name = "Test Tool",
                ServerUrl = new Uri("http://localhost:8000"),
                ToolType = "search",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            };

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
            var toolConfig = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "disabled-tool",
                Name = "Disabled Tool",
                ServerUrl = new Uri("http://localhost:8000"),
                ToolType = "search",
                IsEnabled = false,
                CreatedAt = DateTime.UtcNow
            };

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
            var searchTool = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "search-tool-1",
                Name = "Search Tool 1",
                ServerUrl = new Uri("http://localhost:8000"),
                ToolType = "search",
                IsEnabled = true,
                Priority = 1,
                CreatedAt = DateTime.UtcNow
            };

            var searchTool2 = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "search-tool-2",
                Name = "Search Tool 2",
                ServerUrl = new Uri("http://localhost:8001"),
                ToolType = "search",
                IsEnabled = true,
                Priority = 2,
                CreatedAt = DateTime.UtcNow
            };

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
            var toolConfig = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "test-tool",
                Name = "Test Tool",
                ServerUrl = new Uri("http://localhost:9003"),
                ToolType = "search",
                IsEnabled = true,
                TimeoutMs = 30000,
                CreatedAt = DateTime.UtcNow
            };

            // Act
            var result = await _provider.ValidateToolAsync(toolConfig);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task ValidateToolAsync_DisabledTool_ReturnsFalse()
        {
            // Arrange
            var toolConfig = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "disabled-tool",
                Name = "Disabled Tool",
                ServerUrl = new Uri("http://localhost:8000"),
                ToolType = "search",
                IsEnabled = false,
                CreatedAt = DateTime.UtcNow
            };

            // Act
            var result = await _provider.ValidateToolAsync(toolConfig);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateToolAsync_NoServerUrl_ReturnsFalse()
        {
            // Arrange
            var toolConfig = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "test-tool",
                Name = "Test Tool",
                ServerUrl = null!,
                ToolType = "search",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            };

            // Act
            var result = await _provider.ValidateToolAsync(toolConfig);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateToolAsync_InvalidServerUrl_ReturnsFalse()
        {
            // Arrange
            var toolConfig = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "test-tool",
                Name = "Test Tool",
                ServerUrl = new Uri("not-a-valid-url", UriKind.RelativeOrAbsolute),
                ToolType = "search",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            };

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
            var toolConfig = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "test-tool",
                Name = "Test Tool",
                ServerUrl = new Uri("http://localhost:8000"),
                ToolType = "search",
                IsEnabled = true,
                TimeoutMs = 0,
                CreatedAt = DateTime.UtcNow
            };

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
            var tool = new McpToolConfiguration
            {
                Id = Guid.NewGuid(),
                ToolId = "test-tool",
                Name = "Test Tool",
                ServerUrl = new Uri("http://localhost:8000"),
                ToolType = "search",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            };

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
