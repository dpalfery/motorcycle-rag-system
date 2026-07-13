using Microsoft.AspNetCore.Http;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Tests.Api.Controllers;

public class LegacyPdfIngestionGuardTests
{
    private static IFormFile File(string? fileName, string? contentType = "application/octet-stream")
    {
        var mock = new Mock<IFormFile>();
        mock.SetupGet(f => f.FileName).Returns(fileName!);
        mock.SetupGet(f => f.ContentType).Returns(contentType!);
        return mock.Object;
    }

    [Fact]
    public void IsLegacyPdfRequest_NullRequest_Throws()
        => ((Action)(() => LegacyPdfIngestionGuard.IsLegacyPdfRequest(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void IsLegacyPdfRequest_FileTypePdf_ReturnsTrue()
        => LegacyPdfIngestionGuard.IsLegacyPdfRequest(new DataPipelineRequest { FileType = FileType.PDF }).Should().BeTrue();

    [Fact]
    public void IsLegacyPdfRequest_PdfFileName_ReturnsTrue()
        => LegacyPdfIngestionGuard.IsLegacyPdfRequest(new DataPipelineRequest { FileName = "doc.PDF" }).Should().BeTrue();

    [Fact]
    public void IsLegacyPdfRequest_PdfFilePath_ReturnsTrue()
        => LegacyPdfIngestionGuard.IsLegacyPdfRequest(new DataPipelineRequest { FilePath = "blob-key.pdf" }).Should().BeTrue();

    [Fact]
    public void IsLegacyPdfRequest_NonPdf_ReturnsFalse()
        => LegacyPdfIngestionGuard.IsLegacyPdfRequest(new DataPipelineRequest { FileType = FileType.CSV, FileName = "doc.html" }).Should().BeFalse();

    [Fact]
    public void HasLegacyPdfRequests_Null_Throws()
        => ((Action)(() => LegacyPdfIngestionGuard.HasLegacyPdfRequests(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void HasLegacyPdfRequests_AnyPdf_ReturnsTrue()
        => LegacyPdfIngestionGuard.HasLegacyPdfRequests(new[]
        {
            new DataPipelineRequest { FileType = FileType.CSV },
            new DataPipelineRequest { FileName = "a.pdf" }
        }).Should().BeTrue();

    [Fact]
    public void HasLegacyPdfRequests_NoPdf_ReturnsFalse()
        => LegacyPdfIngestionGuard.HasLegacyPdfRequests(new[]
        {
            new DataPipelineRequest { FileType = FileType.CSV }
        }).Should().BeFalse();

    [Fact]
    public void IsLegacyPdfFile_Null_Throws()
        => ((Action)(() => LegacyPdfIngestionGuard.IsLegacyPdfFile(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void IsLegacyPdfFile_PdfByName_ReturnsTrue()
        => LegacyPdfIngestionGuard.IsLegacyPdfFile(File("doc.pdf")).Should().BeTrue();

    [Fact]
    public void IsLegacyPdfFile_PdfByContentType_ReturnsTrue()
        => LegacyPdfIngestionGuard.IsLegacyPdfFile(File("doc.bin", "application/pdf")).Should().BeTrue();

    [Fact]
    public void IsLegacyPdfFile_NonPdf_ReturnsFalse()
        => LegacyPdfIngestionGuard.IsLegacyPdfFile(File("doc.txt", "text/plain")).Should().BeFalse();

    [Fact]
    public void HasLegacyPdfFiles_Null_Throws()
        => ((Action)(() => LegacyPdfIngestionGuard.HasLegacyPdfFiles(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void HasLegacyPdfFiles_AnyPdf_ReturnsTrue()
        => LegacyPdfIngestionGuard.HasLegacyPdfFiles(new[] { File("a.txt"), File("b.pdf") }).Should().BeTrue();

    [Fact]
    public void HasLegacyPdfFiles_NoPdf_ReturnsFalse()
        => LegacyPdfIngestionGuard.HasLegacyPdfFiles(new[] { File("a.txt") }).Should().BeFalse();

    [Fact]
    public void CreateProblemDetails_ReturnsBadRequestProblem()
    {
        var pd = LegacyPdfIngestionGuard.CreateProblemDetails();
        pd.Status.Should().Be(StatusCodes.Status400BadRequest);
        pd.Title.Should().NotBeNullOrEmpty();
        pd.Detail.Should().NotBeNullOrEmpty();
    }
}
