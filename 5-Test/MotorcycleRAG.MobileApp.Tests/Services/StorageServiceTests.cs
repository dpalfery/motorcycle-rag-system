using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using MotorcycleRAG.MobileApp.Persistence.Repositories;
using MotorcycleRAG.MobileApp.Services;
using Xunit;

namespace MotorcycleRAG.MobileApp.Tests.Services
{
    public class StorageServiceTests
    {
        private readonly Mock<IConversationRepository> _mockConversationRepository;
        private readonly Mock<IMessageRepository> _mockMessageRepository;
        private readonly Mock<IUserMemoryService> _mockUserMemoryService;
        private readonly StorageService _service;

        public StorageServiceTests()
        {
            _mockConversationRepository = new Mock<IConversationRepository>();
            _mockMessageRepository = new Mock<IMessageRepository>();
            _mockUserMemoryService = new Mock<IUserMemoryService>();

            _service = new StorageService(
                _mockConversationRepository.Object,
                _mockMessageRepository.Object,
                _mockUserMemoryService.Object);
        }

        [Fact]
        public async Task PruneIfNeededAsync_ShouldExtractMemoryBeforeDeleting()
        {
            // Arrange
            long maxStorage = 100 * 1024 * 1024;
            long currentUsage = maxStorage + 1000; // Over limit

            var conversationToDelete = new ConversationEntity
            {
                Id = "conv1",
                SizeBytes = 2000,
                UpdatedAt = DateTime.UtcNow.AddDays(-10).ToString("O")
            };

            var messages = new List<MessageEntity>
            {
                new MessageEntity { Content = "I own a Honda", Sender = 0 } // User message
            };

            _mockConversationRepository.Setup(r => r.GetTotalStorageSizeAsync())
                .ReturnsAsync(currentUsage);

            // Mock getting oldest conversations - we'll need to expose this or simulate it
            // Since StorageService currently calls PruneOldestConversationsAsync on repo,
            // we need to change the implementation to handle logic in service.
            // Let's assume we change repo to return candidates or we use GetAll and filter.

            _mockConversationRepository.Setup(r => r.GetAllAsync())
                .ReturnsAsync(new List<ConversationEntity> { conversationToDelete });

            _mockMessageRepository.Setup(r => r.GetByConversationIdAsync("conv1"))
                .ReturnsAsync(messages);

            // Act
            await _service.PruneIfNeededAsync();

            // Assert
            // Should extract memory
            _mockUserMemoryService.Verify(s => s.ExtractFromConversationAsync("conv1", "I own a Honda"), Times.Once);

            // Should delete conversation
            _mockConversationRepository.Verify(r => r.DeleteAsync("conv1"), Times.Once);
        }
    }
}
