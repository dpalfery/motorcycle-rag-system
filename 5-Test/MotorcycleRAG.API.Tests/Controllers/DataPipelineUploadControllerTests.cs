using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Controllers;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class DataPipelineUploadControllerTests
{
    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new DataPipelineUploadController(null!);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("logger");
    }

    [Fact]
    public void UploadAsync_AlwaysReturns410GoneDirectingToBlobBackedEndpoint()
    {
        var sut = new DataPipelineUploadController(NullLogger<DataPipelineUploadController>.Instance);

        var result = sut.UploadAsync(CreateFormFile("manual.pdf"));

        var gone = result.Should().BeOfType<ObjectResult>().Subject;
        gone.StatusCode.Should().Be(StatusCodes.Status410Gone);
        var problem = gone.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status410Gone);
        problem.Title.Should().Be("Legacy disk upload is no longer supported");
        problem.Detail.Should().Contain("/api/ingestion/jobs/upload");
    }

    private static IFormFile CreateFormFile(string fileName)
    {
        using var stream = new MemoryStream("%PDF-1.7"u8.ToArray());
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }
}
