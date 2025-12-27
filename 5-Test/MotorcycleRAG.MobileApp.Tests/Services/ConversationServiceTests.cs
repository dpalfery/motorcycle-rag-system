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
    public class ConversationServiceTests
    {
        private readonly Mock<IConversationRepository> _mockConversationRepository;
        private readonly Mock<IMessageRepository> _mockMessageRepository;
        private readonly Mock<IApiClient> _mockApiClient;
        private readonly ConversationService _service;

        public ConversationServiceTests()
        {
            _mockConversationRepository = new Mock<IConversationRepository>();
            _mockMessageRepository = new Mock<IMessageRepository>();
            _mockApiClient = new Mock<IApiClient>();

            _service = new ConversationService(
                _mockConversationRepository.Object,
                _mockMessageRepository.Object,
                _mockApiClient.Object);
        }

        [Fact]
        public async Task CreateConversationAsync_ShouldReturnNewSession()
        {
            // Arrange
            _mockConversationRepository.Setup(r => r.InsertAsync(It.IsAny<ConversationEntity>()))
                .ReturnsAsync(1);

            // Act
            var result = await _service.CreateConversationAsync();

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().NotBeEmpty();
            result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockConversationRepository.Verify(r => r.InsertAsync(It.IsAny<ConversationEntity>()), Times.Once);
        }

        [Fact]
        public async Task SendMessageAsync_ShouldSaveUserMessageAndReturnResponse()
        {
            // Arrange
            var conversationId = Guid.NewGuid();
            var messageText = "Test question";
            var apiResponse = new QueryResponse
            {
                Response = "Test answer",
                Sources = new List<SearchResult>()
            };

            _mockApiClient.Setup(c => c.QueryAsync(It.IsAny<QueryRequest>()))
                .ReturnsAsync(apiResponse);

            _mockMessageRepository.Setup(r => r.InsertAsync(It.IsAny<MessageEntity>()))
                .ReturnsAsync(1);

            _mockMessageRepository.Setup(r => r.GetByConversationIdAsync(conversationId.ToString()))
                .ReturnsAsync(new List<MessageEntity>());

            // Act
            var result = await _service.SendMessageAsync(conversationId, messageText);

            // Assert
            result.Should().NotBeNull();
            result.Content.Should().Be("Test answer");
            result.Sender.Should().Be(MessageSender.System);

            _mockMessageRepository.Verify(r => r.InsertAsync(It.Is<MessageEntity>(m =>
                m.ConversationId == conversationId.ToString() &&
                m.Content == messageText &&
                m.Sender == (int)MessageSender.User)), Times.Once);

            _mockMessageRepository.Verify(r => r.InsertAsync(It.Is<MessageEntity>(m =>
                m.ConversationId == conversationId.ToString() &&
                m.Content == "Test answer" &&
                m.Sender == (int)MessageSender.System)), Times.Once);
        }

        [Fact]
        public async Task SendMessageAsync_ShouldIncludePreviousQueries_InContext()
        {
            // Arrange
            var conversationId = Guid.NewGuid();
            var messageText = "Follow up question";
            var previousMessages = new List<MessageEntity>
            {
                new MessageEntity { Content = "First question", Sender = (int)MessageSender.User, Timestamp = DateTime.UtcNow.AddMinutes(-5).ToString("O") },
                new MessageEntity { Content = "First answer", Sender = (int)MessageSender.System, Timestamp = DateTime.UtcNow.AddMinutes(-4).ToString("O") },
                new MessageEntity { Content = "Second question", Sender = (int)MessageSender.User, Timestamp = DateTime.UtcNow.AddMinutes(-3).ToString("O") }
            };

            _mockMessageRepository.Setup(r => r.GetByConversationIdAsync(conversationId.ToString()))
                .ReturnsAsync(previousMessages);

            _mockApiClient.Setup(c => c.QueryAsync(It.IsAny<QueryRequest>()))
                .ReturnsAsync(new QueryResponse { Response = "Answer" });

            // Act
            await _service.SendMessageAsync(conversationId, messageText);

            // Assert
            _mockApiClient.Verify(c => c.QueryAsync(It.Is<QueryRequest>(req =>
                req.Context != null &&
                req.Context.PreviousQueries.Count == 2 &&
                req.Context.PreviousQueries[0] == "First question" &&
                req.Context.PreviousQueries[1] == "Second question"
            )), Times.Once);
        }
    }
}
