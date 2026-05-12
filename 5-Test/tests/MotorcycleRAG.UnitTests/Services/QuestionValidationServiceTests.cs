using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services.QueryValidation;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

public class QuestionValidationServiceTests
{
    private readonly Mock<IBikeModelRepository> _bikeModels = new(MockBehavior.Strict);
    private readonly Mock<IGraphRepository> _graph = new(MockBehavior.Strict);
    private readonly Mock<ILogger<QuestionValidationService>> _logger = new();

    private QuestionValidationService CreateService() =>
        new(_bikeModels.Object, _graph.Object, _logger.Object);

    [Fact]
    public async Task ValidateAsync_ExactBikeMatch_AllowsSearch()
    {
        _bikeModels
            .Setup(r => r.ListAsync(0, 5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BikeModel { Make = "Ducati", Model = "Panigale V4", Year = 2026 }
            ]);

        _graph
            .Setup(g => g.SearchNodesAsync(It.IsAny<string>(), "Motorcycle", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GraphNode>());

        var result = await CreateService().ValidateAsync(
            "what are the specs on the 2026 Ducati Panigale V4",
            [],
            CancellationToken.None);

        Assert.True(result.MaySearch);
        Assert.Equal("Answer", result.ResponseType);
        Assert.Equal("MotorcycleSpecs", result.Subject);
    }

    [Fact]
    public async Task ValidateAsync_AliasMatch_AllowsSearch()
    {
        _bikeModels
            .Setup(r => r.ListAsync(0, 5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BikeModel { Make = "Honda", Model = "CBR 1000RR", Year = 2026, Aliases = "CBR1000RR,Fireblade" }
            ]);

        _graph
            .Setup(g => g.SearchNodesAsync(It.IsAny<string>(), "Motorcycle", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GraphNode>());

        var result = await CreateService().ValidateAsync(
            "2026 honda cbr1000rr specs",
            [],
            CancellationToken.None);

        Assert.True(result.MaySearch);
        Assert.Equal("Answer", result.ResponseType);
    }

    [Fact]
    public async Task ValidateAsync_DucatiVr4_ReturnsClarificationSuggestions()
    {
        _bikeModels
            .Setup(r => r.ListAsync(0, 5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BikeModel { Make = "Ducati", Model = "Panigale V4", Year = 2026 },
                new BikeModel { Make = "Aprilia", Model = "RSV4", Year = 2026 }
            ]);

        _graph
            .Setup(g => g.SearchNodesAsync(It.IsAny<string>(), "Motorcycle", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GraphNode>());

        var result = await CreateService().ValidateAsync(
            "what are the specs on the 2026 ducati vr4",
            [],
            CancellationToken.None);

        Assert.False(result.MaySearch);
        Assert.Equal("Clarification", result.ResponseType);
        Assert.Contains(result.Suggestions, s => s.Label.Contains("Ducati", StringComparison.OrdinalIgnoreCase) && s.Label.Contains("Panigale V4", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Suggestions, s => s.Label.Contains("Aprilia", StringComparison.OrdinalIgnoreCase) && s.Label.Contains("RSV4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_TripIntent_ReturnsClarificationWithoutBikeLookup()
    {
        var result = await CreateService().ValidateAsync(
            "plan a motorcycle trip through the smoky mountains",
            [],
            CancellationToken.None);

        Assert.False(result.MaySearch);
        Assert.Equal("Clarification", result.ResponseType);
        Assert.Equal("TripPlanning", result.Subject);
        Assert.NotEmpty(result.Suggestions);
        _bikeModels.VerifyNoOtherCalls();
        _graph.VerifyNoOtherCalls();
    }
}
