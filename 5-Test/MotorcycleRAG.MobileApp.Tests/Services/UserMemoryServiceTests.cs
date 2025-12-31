using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using MotorcycleRAG.MobileApp.Persistence.Repositories;
using MotorcycleRAG.MobileApp.Services;
using Xunit;

namespace MotorcycleRAG.MobileApp.Tests.Services
{
    public class UserMemoryServiceTests
    {
        private readonly Mock<IUserMemoryRepository> _mockRepository;
        private readonly UserMemoryService _service;

        public UserMemoryServiceTests()
        {
            _mockRepository = new Mock<IUserMemoryRepository>();
            _service = new UserMemoryService(_mockRepository.Object);
        }

        [Fact]
        public async Task ExtractFromConversationAsync_ShouldExtractMotorcycleOwned()
        {
            // Arrange
            var conversationId = Guid.NewGuid().ToString();
            var message = "I own a 2023 Yamaha R1";

            _mockRepository.Setup(r => r.GetByCategoryAsync("motorcycles_owned"))
                .ReturnsAsync((UserMemoryEntity?)null);

            // Act
            await _service.ExtractFromConversationAsync(conversationId, message);

            // Assert
            _mockRepository.Verify(r => r.InsertAsync(It.Is<UserMemoryEntity>(m =>
                m.Category == "motorcycles_owned" &&
                m.Value == "2023 Yamaha R1" &&
                m.SourceConversationId == conversationId &&
                m.IsActive == 1)), Times.Once);
        }

        [Fact]
        public async Task ExtractFromConversationAsync_ShouldExtractRidingStyle()
        {
            // Arrange
            var conversationId = Guid.NewGuid().ToString();
            var message = "I prefer sport riding";

            _mockRepository.Setup(r => r.GetByCategoryAsync("riding_style"))
                .ReturnsAsync((UserMemoryEntity?)null);

            // Act
            await _service.ExtractFromConversationAsync(conversationId, message);

            // Assert
            _mockRepository.Verify(r => r.InsertAsync(It.Is<UserMemoryEntity>(m =>
                m.Category == "riding_style" &&
                m.Value == "sport" &&
                m.SourceConversationId == conversationId)), Times.Once);
        }

        [Fact]
        public async Task ExtractFromConversationAsync_ShouldExtractExpertiseLevel()
        {
            // Arrange
            var conversationId = Guid.NewGuid().ToString();
            var message = "I am an intermediate rider";

            _mockRepository.Setup(r => r.GetByCategoryAsync("expertise_level"))
                .ReturnsAsync((UserMemoryEntity?)null);

            // Act
            await _service.ExtractFromConversationAsync(conversationId, message);

            // Assert
            _mockRepository.Verify(r => r.InsertAsync(It.Is<UserMemoryEntity>(m =>
                m.Category == "expertise_level" &&
                m.Value == "intermediate" &&
                m.SourceConversationId == conversationId)), Times.Once);
        }

        [Fact]
        public async Task ExtractFromConversationAsync_ShouldExtractMaintenancePreference()
        {
            // Arrange
            var conversationId = Guid.NewGuid().ToString();
            var message = "I do my own maintenance";

            _mockRepository.Setup(r => r.GetByCategoryAsync("maintenance_preference"))
                .ReturnsAsync((UserMemoryEntity?)null);

            // Act
            await _service.ExtractFromConversationAsync(conversationId, message);

            // Assert
            _mockRepository.Verify(r => r.InsertAsync(It.Is<UserMemoryEntity>(m =>
                m.Category == "maintenance_preference" &&
                m.Value == "does own maintenance" &&
                m.SourceConversationId == conversationId)), Times.Once);
        }

        [Fact]
        public async Task ExtractFromConversationAsync_ShouldUpdateExistingMemory_WhenConflictOccurs()
        {
            // Arrange
            var conversationId = Guid.NewGuid().ToString();
            var message = "I own a Ducati Panigale";
            var existingMemory = new UserMemoryEntity
            {
                Id = "old-id",
                Category = "motorcycles_owned",
                Value = "Yamaha R1",
                IsActive = 1
            };

            _mockRepository.Setup(r => r.GetByCategoryAsync("motorcycles_owned"))
                .ReturnsAsync(existingMemory);

            // Act
            await _service.ExtractFromConversationAsync(conversationId, message);

            // Assert
            // Should deactivate old memory
            _mockRepository.Verify(r => r.DeactivateAsync("old-id"), Times.Once);

            // Should insert new memory
            _mockRepository.Verify(r => r.InsertAsync(It.Is<UserMemoryEntity>(m =>
                m.Category == "motorcycles_owned" &&
                m.Value == "Ducati Panigale" &&
                m.SourceConversationId == conversationId)), Times.Once);
        }

        [Fact]
        public async Task GetActiveMemoriesAsync_ShouldReturnMappedModels()
        {
            // Arrange
            var entities = new List<UserMemoryEntity>
            {
                new UserMemoryEntity { Id = "1", Category = "cat1", Value = "val1", IsActive = 1, ExtractedAt = DateTime.UtcNow.ToString("O") },
                new UserMemoryEntity { Id = "2", Category = "cat2", Value = "val2", IsActive = 1, ExtractedAt = DateTime.UtcNow.ToString("O") }
            };

            _mockRepository.Setup(r => r.GetActiveMemoriesAsync())
                .ReturnsAsync(entities);

            // Act
            var result = await _service.GetActiveMemoriesAsync();

            // Assert
            result.Should().HaveCount(2);
            result[0].Category.Should().Be("cat1");
            result[1].Value.Should().Be("val2");
        }
    }
}
