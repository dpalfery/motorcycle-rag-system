using System;
using System.Threading.Tasks;
using FluentAssertions;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using MotorcycleRAG.MobileApp.Tests.Mocks;
using MotorcycleRAG.MobileApp.ViewModels;
using Xunit;

namespace MotorcycleRAG.MobileApp.Tests.ViewModels;

public class ConversationListViewModelTests
{
    private readonly MockConversationRepository _repository;
    private readonly ConversationListViewModel _viewModel;

    public ConversationListViewModelTests()
    {
        _repository = new MockConversationRepository();
        _viewModel = new ConversationListViewModel(_repository);
    }

    [Fact]
    public async Task LoadConversationsCommand_ShouldLoadAllConversations()
    {
        // Arrange
        _repository.Conversations.Add(new ConversationEntity { Id = "1", Title = "C1", UpdatedAt = "2023-01-01" });
        _repository.Conversations.Add(new ConversationEntity { Id = "2", Title = "C2", UpdatedAt = "2023-01-02" });

        // Act
        await _viewModel.LoadConversationsCommand.ExecuteAsync(null);

        // Assert
        _viewModel.Conversations.Should().HaveCount(2);
        _viewModel.Conversations[0].Title.Should().Be("C2"); // Ordered by UpdatedAt DESC
    }

    [Fact]
    public async Task SearchCommand_ShouldFilterConversations()
    {
        // Arrange
        _repository.Conversations.Add(new ConversationEntity { Id = "1", Title = "Yamaha R1", UpdatedAt = "2023-01-01" });
        _repository.Conversations.Add(new ConversationEntity { Id = "2", Title = "Honda CBR", UpdatedAt = "2023-01-02" });

        // Act
        _viewModel.SearchQuery = "Yamaha";
        await _viewModel.SearchCommand.ExecuteAsync(null);

        // Assert
        _viewModel.Conversations.Should().HaveCount(1);
        _viewModel.Conversations[0].Title.Should().Be("Yamaha R1");
    }

    [Fact]
    public async Task SearchCommand_WithEmptyQuery_ShouldLoadAll()
    {
        // Arrange
        _repository.Conversations.Add(new ConversationEntity { Id = "1", Title = "Yamaha R1" });
        _repository.Conversations.Add(new ConversationEntity { Id = "2", Title = "Honda CBR" });

        // Act
        _viewModel.SearchQuery = "";
        await _viewModel.SearchCommand.ExecuteAsync(null);

        // Assert
        _viewModel.Conversations.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteConversationCommand_ShouldRemoveFromRepositoryAndCollection()
    {
        // Arrange
        var conversation = new ConversationEntity { Id = "1", Title = "To Delete" };
        _repository.Conversations.Add(conversation);
        await _viewModel.LoadConversationsCommand.ExecuteAsync(null);

        // Act
        await _viewModel.DeleteConversationCommand.ExecuteAsync(conversation);

        // Assert
        _repository.Conversations.Should().BeEmpty();
        _viewModel.Conversations.Should().BeEmpty();
    }
}
