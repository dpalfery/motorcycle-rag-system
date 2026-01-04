using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;


namespace MotorcycleRAG.UnitTests.Services
{
    /// <summary>
    /// Unit tests for UsageTrackingService
    /// </summary>
    public class UsageTrackingServiceTests
    {
        private readonly Mock<IUsageRepository> _mockUsageRepository;
        private readonly Mock<ILogger<UsageTrackingService>> _mockLogger;
        private readonly UsageTrackingService _service;

        public UsageTrackingServiceTests()
        {
            _mockUsageRepository = new Mock<IUsageRepository>();
            _mockLogger = new Mock<ILogger<UsageTrackingService>>();

            _service = new UsageTrackingService(
                _mockUsageRepository.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task RecordUsageAsync_ValidUsage_CallsRepository()
        {
            // Arrange
            var usage = new Usage
            {
                UserId = "user-1",
                Endpoint = "/api/test",
                HttpMethod = "GET",
                QueryId = "query-1",
                RequestTime = DateTime.UtcNow,
                DurationMs = 100,
                StatusCode = 200,
                IsSuccess = true
            };

            var recordedUsage = new Usage
            {
                Id = 1,
                UserId = usage.UserId,
                Endpoint = usage.Endpoint,
                HttpMethod = usage.HttpMethod,
                QueryId = usage.QueryId,
                RequestTime = usage.RequestTime,
                DurationMs = usage.DurationMs,
                StatusCode = usage.StatusCode,
                IsSuccess = usage.IsSuccess
            };

            _mockUsageRepository
                .Setup(r => r.RecordUsageAsync(It.IsAny<Usage>()))
                .ReturnsAsync(recordedUsage);

            // Act
            var result = await _service.RecordUsageAsync(
                usage.UserId,
                usage.Endpoint,
                usage.HttpMethod,
                usage.QueryId,
                usage.DurationMs,
                usage.StatusCode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(usage.UserId, result.UserId);
            Assert.Equal(usage.Endpoint, result.Endpoint);
            Assert.Equal(usage.HttpMethod, result.HttpMethod);
            _mockUsageRepository.Verify(r => r.RecordUsageAsync(It.IsAny<Usage>()), Times.Once);
        }

        [Fact]
        public async Task RecordUsageAsync_ThrowsOnNullUserId()
        {
            // Arrange, Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.RecordUsageAsync(null!, "/api/test", "GET"));
        }

        [Fact]
        public async Task RecordUsageAsync_ThrowsOnEmptyUserId()
        {
            // Arrange, Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.RecordUsageAsync(string.Empty, "/api/test", "GET"));
        }

        [Fact]
        public async Task RecordUsageAsync_ThrowsOnNullEndpoint()
        {
            // Arrange, Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.RecordUsageAsync("user-1", null!, "GET"));
        }

        [Fact]
        public async Task RecordUsageAsync_ThrowsOnEmptyEndpoint()
        {
            // Arrange, Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.RecordUsageAsync("user-1", string.Empty, "GET"));
        }

        [Fact]
        public async Task RecordUsageAsync_ThrowsOnNullHttpMethod()
        {
            // Arrange, Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.RecordUsageAsync("user-1", "/api/test", null!));
        }

        [Fact]
        public async Task RecordSuccessAsync_CallsRepositoryWithSuccessStatus()
        {
            // Arrange
            var recordedUsage = new Usage
            {
                Id = 1,
                UserId = "user-1",
                Endpoint = "/api/test",
                HttpMethod = "GET",
                QueryId = "query-1",
                RequestTime = DateTime.UtcNow,
                DurationMs = 100,
                StatusCode = 200,
                IsSuccess = true
            };

            _mockUsageRepository
                .Setup(r => r.RecordUsageAsync(It.IsAny<Usage>()))
                .ReturnsAsync(recordedUsage);

            // Act
            var result = await _service.RecordSuccessAsync(
                "user-1",
                "/api/test",
                "GET",
                "query-1",
                100);

            // Assert
            Assert.Equal(200, result.StatusCode);
            Assert.True(result.IsSuccess);
            _mockUsageRepository.Verify(r => r.RecordUsageAsync(It.Is<Usage>(u => 
                u.StatusCode == 200 && u.IsSuccess == true)), Times.Once);
        }

        [Fact]
        public async Task RecordFailureAsync_CallsRepositoryWithFailureStatus()
        {
            // Arrange
            var recordedUsage = new Usage
            {
                Id = 1,
                UserId = "user-1",
                Endpoint = "/api/test",
                HttpMethod = "GET",
                QueryId = "query-1",
                RequestTime = DateTime.UtcNow,
                DurationMs = 100,
                StatusCode = 400,
                IsSuccess = false
            };

            _mockUsageRepository
                .Setup(r => r.RecordUsageAsync(It.IsAny<Usage>()))
                .ReturnsAsync(recordedUsage);

            // Act
            var result = await _service.RecordFailureAsync(
                "user-1",
                "/api/test",
                "GET",
                400,
                "query-1",
                100);

            // Assert
            Assert.Equal(400, result.StatusCode);
            Assert.False(result.IsSuccess);
            _mockUsageRepository.Verify(r => r.RecordUsageAsync(It.Is<Usage>(u => 
                u.StatusCode == 400 && u.IsSuccess == false)), Times.Once);
        }

        [Fact]
        public async Task GetUsageByDateRangeAsync_ValidParameters_ReturnsUsageRecords()
        {
            // Arrange
            var userId = "user-1";
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow;
            var usageRecords = new[]
            {
                new Usage { Id = 1, UserId = userId, Endpoint = "/api/test", HttpMethod = "GET" },
                new Usage { Id = 2, UserId = userId, Endpoint = "/api/test", HttpMethod = "POST" }
            };

            _mockUsageRepository
                .Setup(r => r.GetUsageByUserAndDateRangeAsync(userId, startDate, endDate))
                .ReturnsAsync(usageRecords);

            // Act
            var result = await _service.GetUsageByDateRangeAsync(userId, startDate, endDate);

            // Assert
            Assert.Equal(2, result.Length);
            _mockUsageRepository.Verify(r => r.GetUsageByUserAndDateRangeAsync(userId, startDate, endDate), Times.Once);
        }

        [Fact]
        public async Task GetUsageByDateRangeAsync_ThrowsOnNullUserId()
        {
            // Arrange
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.GetUsageByDateRangeAsync(null!, startDate, endDate));
        }

        [Fact]
        public async Task GetUsageByDateRangeAsync_ThrowsOnEmptyUserId()
        {
            // Arrange
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.GetUsageByDateRangeAsync(string.Empty, startDate, endDate));
        }

        [Fact]
        public async Task GetUsageByDateRangeAsync_ThrowsOnInvalidDateRange()
        {
            // Arrange
            var userId = "user-1";
            var startDate = DateTime.UtcNow;
            var endDate = DateTime.UtcNow.AddDays(-7);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.GetUsageByDateRangeAsync(userId, startDate, endDate));
        }
    }
}
