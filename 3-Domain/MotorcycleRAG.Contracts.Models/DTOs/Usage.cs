using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs
{
    /// <summary>
    /// Usage tracking model for API calls
    /// </summary>
    public class Usage
    {
        /// <summary>
        /// Unique identifier for the usage record
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// User ID who made the request
        /// </summary>
        [Required]
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// API endpoint that was called
        /// </summary>
        [Required]
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// HTTP method used
        /// </summary>
        [Required]
        public string HttpMethod { get; set; } = string.Empty;

        /// <summary>
        /// Query ID for tracking
        /// </summary>
        public string QueryId { get; set; } = string.Empty;

        /// <summary>
        /// Date and time when the request was made
        /// </summary>
        public DateTime RequestTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Duration of the request in milliseconds
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Status code of the response
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Whether the request was successful
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// IP address of the caller
        /// </summary>
        public string? CallerIp { get; set; }

        /// <summary>
        /// User agent string
        /// </summary>
        public string? UserAgent { get; set; }
    }
}
