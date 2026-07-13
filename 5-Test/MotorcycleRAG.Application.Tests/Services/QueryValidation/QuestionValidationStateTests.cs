using System;
using FluentAssertions;
using MotorcycleRAG.Application.Services.QueryValidation;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.QueryValidation;

public sealed class QuestionValidationStateTests
{
    private readonly QuestionValidationState _sut = new();

    // -------------------------------------------------------------------
    // Default state before Initialize
    // -------------------------------------------------------------------

    [Fact]
    public void DefaultState_BeforeInitialize_HasEmptyQueryAndNoMessagesAndIsNotValidated()
    {
        _sut.OriginalQuery.Should().BeEmpty();
        _sut.RecentMessages.Should().BeEmpty();
        _sut.IsValidated.Should().BeFalse();
        _sut.Result.Should().BeNull();
    }

    // -------------------------------------------------------------------
    // Initialize: null query defaults to empty string
    // -------------------------------------------------------------------

    [Fact]
    public void Initialize_NullQuery_DefaultsToEmptyString()
    {
        _sut.Initialize(null!, null);

        _sut.OriginalQuery.Should().BeEmpty();
    }

    // -------------------------------------------------------------------
    // Initialize: filters out null/whitespace-content messages
    // -------------------------------------------------------------------

    [Fact]
    public void Initialize_MixOfValidNullAndWhitespaceContentMessages_KeepsOnlyValidOnes()
    {
        var messages = new QueryRecentMessage?[]
        {
            new QueryRecentMessage { Role = "user", Content = "What is the seat height?" },
            null,
            new QueryRecentMessage { Role = "assistant", Content = "   " },
            new QueryRecentMessage { Role = "assistant", Content = "" },
            new QueryRecentMessage { Role = "assistant", Content = "It is 830mm." }
        };

        _sut.Initialize("what is the seat height", messages!);

        _sut.RecentMessages.Should().HaveCount(2);
        _sut.RecentMessages[0].Content.Should().Be("What is the seat height?");
        _sut.RecentMessages[1].Content.Should().Be("It is 830mm.");
    }

    // -------------------------------------------------------------------
    // Initialize: reusable / reset operation, not additive
    // -------------------------------------------------------------------

    [Fact]
    public void Initialize_CalledAgain_ClearsPriorResultAndRecentMessages()
    {
        _sut.Initialize("first query", new[]
        {
            new QueryRecentMessage { Role = "user", Content = "first message" }
        });
        _sut.Record(new QuestionValidationResult { Subject = "MotorcycleSpecs" });

        _sut.IsValidated.Should().BeTrue();
        _sut.RecentMessages.Should().HaveCount(1);

        _sut.Initialize("second query", null);

        _sut.OriginalQuery.Should().Be("second query");
        _sut.RecentMessages.Should().BeEmpty();
        _sut.Result.Should().BeNull();
        _sut.IsValidated.Should().BeFalse();
    }

    // -------------------------------------------------------------------
    // Record
    // -------------------------------------------------------------------

    [Fact]
    public void Record_ValidResult_SetsResultAndFlipsIsValidatedToTrue()
    {
        var result = new QuestionValidationResult { Subject = "MotorcycleSpecs", MaySearch = true };

        _sut.Record(result);

        _sut.Result.Should().BeSameAs(result);
        _sut.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void Record_NullResult_ThrowsArgumentNullException()
    {
        var act = () => _sut.Record(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
