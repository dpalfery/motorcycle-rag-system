using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Services;
using MotorcycleRAG.MobileApp.ViewModels;
using Xunit;

namespace MotorcycleRAG.MobileApp.Tests.ViewModels
{
    public class ChatViewModelTests
    {
        private readonly Mock<IConversationService> _mockConversationService;
        private readonly ChatViewModel _viewModel;

        public ChatViewModelTests()
        {
            _mockConversationService = new Mock<IConversationService>();
            _viewModel = new ChatViewModel(_mockConversationService.Object);
        }

        [Fact]
        public async Task SendQuestionCommand_ShouldAddUserAndSystemMessages_WhenApiCallSucceeds()
        {
            // Arrange
            var question = "What is the top speed of Yamaha R1?";
            var answer = "The top speed is 299 km/h.";
            var conversationId = Guid.NewGuid();

            _viewModel.ConversationId = conversationId;
            _viewModel.QuestionText = question;

            var assistantMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = conversationId.ToString(),
                Sender = MessageSender.System,
                Content = answer,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Sent
            };

            _mockConversationService.Setup(s => s.SendMessageAsync(conversationId, question))
                .ReturnsAsync(assistantMessage);

            // Act
            await _viewModel.SendQuestionCommand.ExecuteAsync(null);

            // Assert
            _viewModel.Messages.Should().HaveCount(2);
            _viewModel.Messages[0].Content.Should().Be(question);
            _viewModel.Messages[0].Sender.Should().Be(MessageSender.User);
            _viewModel.Messages[1].Content.Should().Be(answer);
            _viewModel.Messages[1].Sender.Should().Be(MessageSender.System);
            _viewModel.QuestionText.Should().BeEmpty();
            _viewModel.IsSending.Should().BeFalse();
        }

        [Fact]
        public async Task SendQuestionCommand_ShouldHandleApiFailure()
        {
            // Arrange
            var question = "What is the top speed?";
            var conversationId = Guid.NewGuid();

            _viewModel.ConversationId = conversationId;
            _viewModel.QuestionText = question;

            _mockConversationService.Setup(s => s.SendMessageAsync(conversationId, question))
                .ThrowsAsync(new Exception("API Error"));

            // Act
            await _viewModel.SendQuestionCommand.ExecuteAsync(null);

            // Assert
            _viewModel.Messages.Should().HaveCount(1); // Only user message added initially
            _viewModel.Messages[0].Status.Should().Be(MessageStatus.Failed);
            _viewModel.IsSending.Should().BeFalse();
            // In a real app we might show a dialog or toast, but here we check state
        }

        [Fact]
        public async Task SendQuestionCommand_ShouldCreateConversation_IfIdIsEmpty()
        {
            // Arrange
            var question = "New conversation question";
            var newConversationId = Guid.NewGuid();
            var newSession = new ConversationSession { Id = newConversationId.ToString() };
            var assistantMessage = new ChatMessage { Content = "Answer" };

            _viewModel.ConversationId = Guid.Empty;
            _viewModel.QuestionText = question;

            _mockConversationService.Setup(s => s.CreateConversationAsync())
                .ReturnsAsync(newSession);

            _mockConversationService.Setup(s => s.SendMessageAsync(newConversationId, question))
                .ReturnsAsync(assistantMessage);

            // Act
            await _viewModel.SendQuestionCommand.ExecuteAsync(null);

            // Assert
            _viewModel.ConversationId.Should().Be(newConversationId);
            _mockConversationService.Verify(s => s.CreateConversationAsync(), Times.Once);
        }

        [Fact]
        public async Task StartNewChatCommand_ShouldResetState()
        {
            // Arrange
            _viewModel.ConversationId = Guid.NewGuid();
            _viewModel.SessionId = _viewModel.ConversationId.ToString();
            _viewModel.Messages.Add(new ChatMessage());

            // Act
            await _viewModel.StartNewChatCommand.ExecuteAsync(null);

            // Assert
            _viewModel.ConversationId.Should().Be(Guid.Empty);
            _viewModel.SessionId.Should().BeEmpty();
            _viewModel.Messages.Should().BeEmpty();
        }
    }
}
