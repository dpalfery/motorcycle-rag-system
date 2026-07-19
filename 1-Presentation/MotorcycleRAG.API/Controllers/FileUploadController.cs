using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Controller for file upload operations including single and batch file uploads
/// </summary>
[ApiController]
[Route("api/file-upload")]
[Produces("application/json")]
[Authorize(Policy = "mcr-api-admin")] // Require Admin policy for all upload operations
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public class FileUploadController : ControllerBase
{
    private readonly FileUploadConfiguration _fileUploadConfiguration;
    private readonly ILogger<FileUploadController> _logger;

    public FileUploadController(
        IOptions<FileUploadConfiguration> fileUploadConfiguration,
        ILogger<FileUploadController> logger)
    {
        _fileUploadConfiguration = fileUploadConfiguration?.Value ?? throw new ArgumentNullException(nameof(fileUploadConfiguration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get file upload constraints
    /// </summary>
    /// <returns>Upload constraints information</returns>
    [HttpGet("constraints")]
    [ProducesResponseType(typeof(FileUploadConstraints), 200)]
    public IActionResult GetUploadConstraints()
    {
        try
        {
            var constraints = CreateUploadConstraints();
            return Ok(constraints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upload constraints");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving upload constraints",
                Status = 500
            });
        }
    }

    private FileUploadConstraints CreateUploadConstraints()
    {
        var constraints = new FileUploadConstraints
        {
            MaxFileSizeBytes = _fileUploadConfiguration.MaxFileSizeBytes,
            MaxFileSizeDisplay = FormatFileSize(_fileUploadConfiguration.MaxFileSizeBytes),
            MaxFilesPerBatch = _fileUploadConfiguration.MaxFilesPerBatch
        };

        constraints.SupportedFileTypes.Add("CSV");
        constraints.SupportedFileTypes.Add("PDF");

        foreach (var extension in _fileUploadConfiguration.AllowedExtensions)
        {
            constraints.SupportedExtensions.Add(extension.ToLowerInvariant());
        }

        constraints.FileTypeDescriptions["CSV"] = "Comma-separated values files for motorcycle specification data";
        constraints.FileTypeDescriptions["PDF"] = "PDF documents for motorcycle manuals";

        return constraints;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        var len = (double)bytes;
        var order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
