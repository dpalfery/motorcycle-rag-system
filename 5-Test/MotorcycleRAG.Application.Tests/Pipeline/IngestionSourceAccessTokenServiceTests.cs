using Microsoft.Extensions.Caching.Memory;
using MotorcycleRAG.Application.Services.Ingestion;

namespace MotorcycleRAG.UnitTests.Pipeline;

public sealed class IngestionSourceAccessTokenServiceTests
{
    [Fact]
    public void CreateToken_IsValidOnlyForMatchingUploadAndDocumentType()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var sut = new IngestionSourceAccessTokenService(cache);

        var token = sut.CreateToken("upload-123", "manual-pdf");

        sut.IsValid(token, "upload-123", "manual-pdf").Should().BeTrue();
        sut.IsValid(token, "upload-456", "manual-pdf").Should().BeFalse();
        sut.IsValid(token, "upload-123", "spec-dataset").Should().BeFalse();
        sut.IsValid("missing-token", "upload-123", "manual-pdf").Should().BeFalse();
    }
}
