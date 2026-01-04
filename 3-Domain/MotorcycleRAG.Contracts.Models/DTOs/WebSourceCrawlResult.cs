using System;

namespace MotorcycleRAG.Contracts.Models.DTOs {
    /// <summary>
    /// Web source crawl result model
    /// </summary>
    public class WebSourceCrawlResult {
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
