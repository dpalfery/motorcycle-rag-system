using System;
using System.Threading.Tasks;
using FluentAssertions;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using MotorcycleRAG.MobileApp.Persistence.Repositories;
using SQLite;
using Xunit;

namespace MotorcycleRAG.MobileApp.Tests.Persistence;

public class ConversationRepositoryTests : IAsyncLifetime
{
    private SQLiteAsyncConnection _connection;
    private ConversationRepository _repository;

    public async Task InitializeAsync()
    {
        // Use in-memory database
        // Note: In-memory DBs in SQLite might be shared if named, but ":memory:" is usually unique per connection.
        // However, SQLiteAsyncConnection might need a path.
        // For unit tests, we can use a temporary file or ":memory:".
        // Let's try ":memory:" first.
        _connection = new SQLiteAsyncConnection(":memory:");
        await _connection.CreateTableAsync<ConversationEntity>();
        _repository = new ConversationRepository(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.CloseAsync();
    }

    [Fact]
    public async Task InsertAsync_ShouldAddConversation()
    {
        // Arrange
        var entity = new ConversationEntity
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Test Conversation",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            SizeBytes = 100
        };

        // Act
        var result = await _repository.InsertAsync(entity);

        // Assert
        result.Should().Be(1);
        var retrieved = await _repository.GetByIdAsync(entity.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Test Conversation");
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateConversation()
    {
        // Arrange
        var entity = new ConversationEntity
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Original Title",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            SizeBytes = 100
        };
        await _repository.InsertAsync(entity);

        // Act
        entity.Title = "Updated Title";
        var result = await _repository.UpdateAsync(entity);

        // Assert
        result.Should().Be(1);
        var retrieved = await _repository.GetByIdAsync(entity.Id);
        retrieved!.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveConversation()
    {
        // Arrange
        var entity = new ConversationEntity
        {
            Id = Guid.NewGuid().ToString(),
            Title = "To Delete",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        await _repository.InsertAsync(entity);

        // Act
        var result = await _repository.DeleteAsync(entity.Id);

        // Assert
        result.Should().Be(1);
        var retrieved = await _repository.GetByIdAsync(entity.Id);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetTotalStorageSizeAsync_ShouldReturnSumOfSizeBytes()
    {
        // Arrange
        await _repository.InsertAsync(new ConversationEntity { Id = "1", SizeBytes = 100 });
        await _repository.InsertAsync(new ConversationEntity { Id = "2", SizeBytes = 200 });
        await _repository.InsertAsync(new ConversationEntity { Id = "3", SizeBytes = 300 });

        // Act
        var total = await _repository.GetTotalStorageSizeAsync();

        // Assert
        total.Should().Be(600);
    }
}
