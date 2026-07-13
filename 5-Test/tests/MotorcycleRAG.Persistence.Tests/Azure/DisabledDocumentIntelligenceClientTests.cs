using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.Persistence.Tests.Azure;
public class DisabledDocumentIntelligenceClientTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new DisabledDocumentIntelligenceClient(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
    [Fact]
    public void Constructor_ShouldSucceed_WhenLoggerIsProvided()
    {
        var sut = new DisabledDocumentIntelligenceClient(TestHelpers.CreateNullLogger<DisabledDocumentIntelligenceClient>());
        sut.Should().NotBeNull();
    }
    [Fact]
    public async Task AnalyzeDocumentAsync_ByUri_ShouldThrowInvalidOperationException()
    {
        var sut = new DisabledDocumentIntelligenceClient(TestHelpers.CreateNullLogger<DisabledDocumentIntelligenceClient>());
        var act = async () => await sut.AnalyzeDocumentAsync(new Uri("https://example.com/doc.pdf"));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not configured*");
    }
    [Fact]
    public async Task AnalyzeDocumentAsync_ByUri_ShouldThrowArgumentNullException_WhenUriIsNull()
    {
        var sut = new DisabledDocumentIntelligenceClient(TestHelpers.CreateNullLogger<DisabledDocumentIntelligenceClient>());
        var act = async () => await sut.AnalyzeDocumentAsync((Uri)null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("documentUri");
    }
    [Fact]
    public async Task AnalyzeDocumentAsync_ByStream_ShouldThrowInvalidOperationException()
    {
        var sut = new DisabledDocumentIntelligenceClient(TestHelpers.CreateNullLogger<DisabledDocumentIntelligenceClient>());
        using var stream = new MemoryStream();
        var act = async () => await sut.AnalyzeDocumentAsync(stream, "application/pdf");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not configured*");
    }
    [Fact]
    public async Task AnalyzeDocumentAsync_ByStream_ShouldThrowArgumentNullException_WhenStreamIsNull()
    {
        var sut = new DisabledDocumentIntelligenceClient(TestHelpers.CreateNullLogger<DisabledDocumentIntelligenceClient>());
        var act = async () => await sut.AnalyzeDocumentAsync((Stream)null!, "application/pdf");
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("documentStream");
    }
}
