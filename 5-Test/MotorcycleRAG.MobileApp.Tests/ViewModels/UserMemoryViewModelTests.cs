using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Services;
using MotorcycleRAG.MobileApp.ViewModels;
using Xunit;

namespace MotorcycleRAG.MobileApp.Tests.ViewModels
{
    public class UserMemoryViewModelTests
    {
        private readonly Mock<IUserMemoryService> _mockUserMemoryService;
        private readonly Mock<IStorageService> _mockStorageService;
        private readonly Mock<IApiClient> _mockApiClient;
        private readonly UserMemoryViewModel _viewModel;

        public UserMemoryViewModelTests()
        {
            _mockUserMemoryService = new Mock<IUserMemoryService>();
            _mockStorageService = new Mock<IStorageService>();
            _mockApiClient = new Mock<IApiClient>();

            // Defaults
            _mockUserMemoryService.Setup(s => s.GetActiveMemoriesAsync()).ReturnsAsync(new List<UserMemory>());
            _mockStorageService.Setup(s => s.GetUsageBytesAsync()).ReturnsAsync(0);
            _mockApiClient.Setup(c => c.GetUserProfileAsync()).ReturnsAsync(new UserProfile());

            _viewModel = new UserMemoryViewModel(_mockUserMemoryService.Object, _mockStorageService.Object, _mockApiClient.Object);
        }

        [Fact]
        public async Task LoadMemoriesCommand_ShouldPopulateMemories()
        {
            // Arrange
            var memories = new List<UserMemory>
            {
                new UserMemory { Category = "motorcycles_owned", Value = "Yamaha R1" },
                new UserMemory { Category = "riding_style", Value = "Sport" }
            };

            _mockUserMemoryService.Setup(s => s.GetActiveMemoriesAsync())
                .ReturnsAsync(memories);

            // Act
            await _viewModel.LoadMemoriesCommand.ExecuteAsync(null);

            // Assert
            _viewModel.Memories.Should().HaveCount(2);
            _viewModel.Memories[0].Value.Should().Be("Yamaha R1");
            _viewModel.Memories[1].Value.Should().Be("Sport");
        }

        [Fact]
        public async Task LoadMemoriesCommand_ShouldHandleEmptyList()
        {
            // Arrange
            _mockUserMemoryService.Setup(s => s.GetActiveMemoriesAsync())
                .ReturnsAsync(new List<UserMemory>());

            // Act
            await _viewModel.LoadMemoriesCommand.ExecuteAsync(null);

            // Assert
            _viewModel.Memories.Should().BeEmpty();
        }

        [Fact]
        public async Task LoadMemoriesCommand_ShouldLoadStorageUsage()
        {
            // Arrange
            _mockStorageService.Setup(s => s.GetUsageBytesAsync()).ReturnsAsync(50 * 1024 * 1024); // 50MB

            // Act
            await _viewModel.LoadMemoriesCommand.ExecuteAsync(null);

            // Assert
            _viewModel.StorageUsageMB.Should().Be(50.0);
            _viewModel.StorageLimitMB.Should().Be(100.0);
            _viewModel.StorageUsagePercent.Should().Be(0.5);
        }

        [Fact]
        public async Task LoadMemoriesCommand_ShouldLoadUserProfile()
        {
            // Arrange
            var profile = new UserProfile
            {
                Plan = SubscriptionPlan.Pro,
                DailyRequestLimit = 100,
                RequestsUsedToday = 25
            };

            _mockApiClient.Setup(c => c.GetUserProfileAsync()).ReturnsAsync(profile);

            // Act
            await _viewModel.LoadMemoriesCommand.ExecuteAsync(null);

            // Assert
            _viewModel.UserProfile.Should().BeEquivalentTo(profile);
            _viewModel.DailyUsagePercent.Should().Be(0.25);
        }
    }
}
