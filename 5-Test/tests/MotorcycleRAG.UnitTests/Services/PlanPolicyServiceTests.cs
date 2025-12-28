using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services
{
    /// <summary>
    /// Unit tests for PlanPolicyService
    /// </summary>
    public class PlanPolicyServiceTests
    {
        private readonly Mock<IPlanRepository> _mockPlanRepository;
        private readonly Mock<IUsageRepository> _mockUsageRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ILogger<PlanPolicyService>> _mockLogger;
        private readonly PlanPolicyService _service;

        public PlanPolicyServiceTests()
        {
            _mockPlanRepository = new Mock<IPlanRepository>();
            _mockUsageRepository = new Mock<IUsageRepository>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockLogger = new Mock<ILogger<PlanPolicyService>>();

            _service = new PlanPolicyService(
                _mockPlanRepository.Object,
                _mockUsageRepository.Object,
                _mockUserRepository.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task GetDailyRequestLimitAsync_UserWithPlan_ReturnsPlanLimit()
        {
            // Arrange
            var plan = new UserPlan
            {
                Id = "plan-1",
                Name = "Premium",
                DailyRequestLimit = 500,
                IsPaid = true
            };
            var user = new User
            {
                Id = "user-1",
                Email = "test@example.com",
                PlanId = "plan-1"
            };

            _mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("plan-1"))
                .ReturnsAsync(plan);

            // Act
            var result = await _service.GetDailyRequestLimitAsync(user);

            // Assert
            Assert.Equal(500, result);
            _mockPlanRepository.Verify(r => r.GetPlanByIdAsync("plan-1"), Times.Once);
        }

        [Fact]
        public async Task GetDailyRequestLimitAsync_UserWithoutPlan_ReturnsDefaultLimit()
        {
            // Arrange
            var user = new User
            {
                Id = "user-1",
                Email = "test@example.com",
                PlanId = string.Empty
            };

            // Act
            var result = await _service.GetDailyRequestLimitAsync(user);

            // Assert
            Assert.Equal(100, result);
            _mockPlanRepository.Verify(r => r.GetPlanByIdAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetDailyRequestLimitAsync_PlanNotFound_ReturnsDefaultLimit()
        {
            // Arrange
            var user = new User
            {
                Id = "user-1",
                Email = "test@example.com",
                PlanId = "unknown-plan"
            };

            _mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("unknown-plan"))
                .ReturnsAsync((UserPlan?)null);

            // Act
            var result = await _service.GetDailyRequestLimitAsync(user);

            // Assert
            Assert.Equal(100, result);
        }

        [Fact]
        public async Task GetDailyUsageCountAsync_ValidUserId_ReturnsCount()
        {
            // Arrange
            var userId = "user-1";
            var date = DateTime.UtcNow;

            _mockUsageRepository
                .Setup(r => r.GetDailyUsageCountAsync(userId, date))
                .ReturnsAsync(42);

            // Act
            var result = await _service.GetDailyUsageCountAsync(userId, date);

            // Assert
            Assert.Equal(42, result);
            _mockUsageRepository.Verify(r => r.GetDailyUsageCountAsync(userId, date), Times.Once);
        }

        [Fact]
        public async Task GetDailyUsageCountAsync_ThrowsOnNullUserId()
        {
            // Arrange
            var date = DateTime.UtcNow;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.GetDailyUsageCountAsync(null!, date));
        }

        [Fact]
        public async Task GetDailyUsageCountAsync_ThrowsOnEmptyUserId()
        {
            // Arrange
            var date = DateTime.UtcNow;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.GetDailyUsageCountAsync(string.Empty, date));
        }

        [Fact]
        public async Task HasExceededDailyLimitAsync_UsageBelowLimit_ReturnsFalse()
        {
            // Arrange
            var userId = "user-1";
            var date = DateTime.UtcNow;
            var user = new User
            {
                Id = userId,
                Email = "test@example.com",
                PlanId = "plan-1"
            };
            var plan = new UserPlan
            {
                Id = "plan-1",
                Name = "Premium",
                DailyRequestLimit = 100,
                IsPaid = true
            };

            _mockUserRepository
                .Setup(r => r.GetUserByIdAsync(userId))
                .ReturnsAsync(user);
            _mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("plan-1"))
                .ReturnsAsync(plan);
            _mockUsageRepository
                .Setup(r => r.GetDailyUsageCountAsync(userId, date))
                .ReturnsAsync(50);

            // Act
            var result = await _service.HasExceededDailyLimitAsync(userId, date);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task HasExceededDailyLimitAsync_UsageAtLimit_ReturnsTrue()
        {
            // Arrange
            var userId = "user-1";
            var date = DateTime.UtcNow;
            var user = new User
            {
                Id = userId,
                Email = "test@example.com",
                PlanId = "plan-1"
            };
            var plan = new UserPlan
            {
                Id = "plan-1",
                Name = "Premium",
                DailyRequestLimit = 100,
                IsPaid = true
            };

            _mockUserRepository
                .Setup(r => r.GetUserByIdAsync(userId))
                .ReturnsAsync(user);
            _mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("plan-1"))
                .ReturnsAsync(plan);
            _mockUsageRepository
                .Setup(r => r.GetDailyUsageCountAsync(userId, date))
                .ReturnsAsync(100);

            // Act
            var result = await _service.HasExceededDailyLimitAsync(userId, date);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task HasExceededDailyLimitAsync_UsageAboveLimit_ReturnsTrue()
        {
            // Arrange
            var userId = "user-1";
            var date = DateTime.UtcNow;
            var user = new User
            {
                Id = userId,
                Email = "test@example.com",
                PlanId = "plan-1"
            };
            var plan = new UserPlan
            {
                Id = "plan-1",
                Name = "Premium",
                DailyRequestLimit = 100,
                IsPaid = true
            };

            _mockUserRepository
                .Setup(r => r.GetUserByIdAsync(userId))
                .ReturnsAsync(user);
            _mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("plan-1"))
                .ReturnsAsync(plan);
            _mockUsageRepository
                .Setup(r => r.GetDailyUsageCountAsync(userId, date))
                .ReturnsAsync(150);

            // Act
            var result = await _service.HasExceededDailyLimitAsync(userId, date);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task GetRemainingDailyRequestsAsync_ReturnsCorrectValue()
        {
            // Arrange
            var userId = "user-1";
            var date = DateTime.UtcNow;
            var user = new User
            {
                Id = userId,
                Email = "test@example.com",
                PlanId = "plan-1"
            };
            var plan = new UserPlan
            {
                Id = "plan-1",
                Name = "Premium",
                DailyRequestLimit = 100,
                IsPaid = true
            };

            _mockUserRepository
                .Setup(r => r.GetUserByIdAsync(userId))
                .ReturnsAsync(user);
            _mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("plan-1"))
                .ReturnsAsync(plan);
            _mockUsageRepository
                .Setup(r => r.GetDailyUsageCountAsync(userId, date))
                .ReturnsAsync(75);

            // Act
            var result = await _service.GetRemainingDailyRequestsAsync(userId, date);

            // Assert
            Assert.Equal(25, result);
        }

        [Fact]
        public async Task GetRemainingDailyRequestsAsync_NegativeResult_ReturnsZero()
        {
            // Arrange
            var userId = "user-1";
            var date = DateTime.UtcNow;
            var user = new User
            {
                Id = userId,
                Email = "test@example.com",
                PlanId = "plan-1"
            };
            var plan = new UserPlan
            {
                Id = "plan-1",
                Name = "Premium",
                DailyRequestLimit = 100,
                IsPaid = true
            };

            _mockUserRepository
                .Setup(r => r.GetUserByIdAsync(userId))
                .ReturnsAsync(user);
            _mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("plan-1"))
                .ReturnsAsync(plan);
            _mockUsageRepository
                .Setup(r => r.GetDailyUsageCountAsync(userId, date))
                .ReturnsAsync(150);

            // Act
            var result = await _service.GetRemainingDailyRequestsAsync(userId, date);

            // Assert
            Assert.Equal(0, result);
        }
    }
}