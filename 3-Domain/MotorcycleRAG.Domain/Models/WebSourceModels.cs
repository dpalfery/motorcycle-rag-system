using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.Models
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

    /// <summary>
    /// Web source crawl result model
    /// </summary>
    public class WebSourceCrawlResult
    {
        /// <summary>
        /// Unique identifier for the crawl result
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Web source ID
        /// </summary>
        public int WebSourceId { get; set; }

        /// <summary>
        /// Date and time when crawling started
        /// </summary>
        public DateTime CrawlStartTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Date and time when crawling completed
        /// </summary>
        public DateTime? CrawlEndTime { get; set; }

        /// <summary>
        /// Status of the crawl
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Number of pages crawled
        /// </summary>
        public int PagesCrawled { get; set; }

        /// <summary>
        /// Number of pages indexed
        /// </summary>
        public int PagesIndexed { get; set; }

        /// <summary>
        /// Number of errors encountered
        /// </summary>
        public int Errors { get; set; }

        /// <summary>
        /// Error message if crawl failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}