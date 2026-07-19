using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class FileUploadControllerTests
{
    private static readonly string[] AllowedExtensions = [".CSV", ".PDF"];
    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new FileUploadController(null!, NullLogger<FileUploadController>.Instance);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("fileUploadConfiguration");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new FileUploadController(Options.Create(new FileUploadConfiguration()), null!);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("logger");
    }

    [Fact]
    public void GetUploadConstraints_ReturnsOkWithConfiguredConstraintsAndLowercasedExtensions()
    {
        var sut = CreateController(new FileUploadConfiguration
        {
            MaxFileSizeBytes = 50 * 1024 * 1024,
            MaxFilesPerBatch = 7,
            AllowedExtensions = AllowedExtensions
        });

        var result = sut.GetUploadConstraints();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var constraints = ok.Value.Should().BeOfType<FileUploadConstraints>().Subject;
        constraints.MaxFileSizeBytes.Should().Be(50 * 1024 * 1024);
        constraints.MaxFileSizeDisplay.Should().Be("50 MB");
        constraints.MaxFilesPerBatch.Should().Be(7);
        constraints.SupportedFileTypes.Should().Contain("CSV").And.Contain("PDF");
        // Extensions are normalized to lower invariant.
        constraints.SupportedExtensions.Should().Contain(".csv").And.Contain(".pdf");
        constraints.FileTypeDescriptions["CSV"].Should().Contain("specification data");
        constraints.FileTypeDescriptions["PDF"].Should().Contain("manuals");
    }

    [Theory]
    [InlineData(1023L, "1023 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void GetUploadConstraints_FormatsFileSizeAcrossUnitBoundaries(long bytes, string expected)
    {
        var sut = CreateController(new FileUploadConfiguration { MaxFileSizeBytes = bytes });

        var result = sut.GetUploadConstraints();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<FileUploadConstraints>()
            .Which.MaxFileSizeDisplay.Should().Be(expected);
    }

    [Fact]
    public void GetUploadConstraints_WhenConfigurationIsInvalid_ReturnsInternalServerError()
    {
        // AllowedExtensions = null forces CreateUploadConstraints to throw, exercising the 500 catch.
        var sut = CreateController(new FileUploadConfiguration { AllowedExtensions = null! });

        var result = sut.GetUploadConstraints();

        var serverError = result.Should().BeOfType<ObjectResult>().Subject;
        serverError.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        serverError.Value.Should().BeOfType<ProblemDetails>()
            .Which.Title.Should().Be("Internal server error");
    }

    private static FileUploadController CreateController(FileUploadConfiguration? config = null) =>
        new(Options.Create(config ?? new FileUploadConfiguration()), NullLogger<FileUploadController>.Instance);
}
