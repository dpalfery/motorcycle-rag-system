using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs
{
    /// <summary>
    /// Web source model for registered websites
    /// </summary>
    public class WebSource
    {
        /// <summary>
        /// Unique identifier for the web source
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// URL of the web source
        /// </summary>
        [Required]
        [Url]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the web source
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of the web source
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Whether the web source is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Trust tier for the web source (1-5, with 5 being most trusted)
        /// </summary>
        public int TrustTier { get; set; } = 3;

        /// <summary>
        /// Date when the web source was created
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Date when the web source was last updated
        /// </summary>
        public DateTime? LastUpdatedDate { get; set; }

        /// <summary>
        /// Date when the web source was last crawled
        /// </summary>
        public DateTime? LastCrawledDate { get; set; }

        /// <summary>
        /// Crawl frequency in hours
        /// </summary>
        public int CrawlFrequencyHours { get; set; } = 24;

        /// <summary>
        /// Whether to include this source in web search results
        /// </summary>
        public bool IncludeInSearch { get; set; } = true;

        /// <summary>
        /// Maximum depth to crawl
        /// </summary>
        public int MaxCrawlDepth { get; set; } = 2;
    }
}
