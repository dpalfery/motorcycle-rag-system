// <copyright file="Citation.cs" company="MotorcycleRAG">
// Copyright (c) MotorcycleRAG. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Types of citation sources for evidence-based responses
/// </summary>
public enum CitationSourceType
{
    /// <summary>
    /// Structured dataset (CSV, database, etc.)
    /// </summary>
    Dataset,

    /// <summary>
    /// Web page or online article
    /// </summary>
    Website,

    /// <summary>
    /// PDF manual or technical document
    /// </summary>
    ManualPdf,

    /// <summary>
    /// Manufacturer official specifications
    /// </summary>
    ManufacturerSpecs,

    /// <summary>
    /// Industry standard or certification
    /// </summary>
    IndustryStandard,

    /// <summary>
    /// Expert review or analysis
    /// </summary>
    ExpertReview
}

/// <summary>
/// Citation information for evidence-based claims
/// </summary>
public class Citation
{
    /// <summary>
    /// Type of source being cited
    /// </summary>
    [Required]
    public CitationSourceType SourceType { get; set; }

    /// <summary>
    /// Name of the source
    /// </summary>
    [Required]
    [StringLength(200)]
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// URL to the original source
    /// </summary>
    public Uri? SourceUrl { get; set; }

    /// <summary>
    /// Page number in the source document
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Section or chapter name
    /// </summary>
    [StringLength(100)]
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score for this citation (0.0 to 1.0)
    /// </summary>
    [Range(0.0, 1.0)]
    public float ConfidenceScore { get; set; } = 1.0f;

    /// <summary>
    /// Whether this citation has been verified
    /// </summary>
    public bool Verified { get; set; }

    /// <summary>
    /// Method used for verification
    /// </summary>
    [StringLength(200)]
    public string VerificationMethod { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when citation was verified
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Additional metadata about the citation
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Source-specific locator information
    /// </summary>
    public object? Locator { get; set; }
}
