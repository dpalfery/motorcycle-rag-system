using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Application.Services {
    /// <summary>
    /// Service for tracking API usage through repository calls
    /// </summary>
    public class UsageTrackingService : IUsageTrackingService {
        private readonly IUsageRepository _usageRepository;
        private readonly ILogger<UsageTrackingService> _logger;

        /// <summary>
        /// Initializes a new instance of UsageTrackingService
        /// </summary>
        /// <param name="usageRepository">Usage repository</param>
        /// <param name="logger">Logger</param>
        public UsageTrackingService(
            IUsageRepository usageRepository,
            ILogger<UsageTrackingService> logger) {
            _usageRepository = usageRepository ?? throw new ArgumentNullException(nameof(usageRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Records a usage event
        /// </summary>
        public async Task<Usage> RecordUsageAsync(
            string userId,
            string endpoint,
            string httpMethod,
            string? queryId = null,
            long durationMs = 0,
            int statusCode = 200,
            string? callerIp = null,
            string? userAgent = null) {
            if (string.IsNullOrWhiteSpace(userId)) {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(endpoint)) {
                throw new ArgumentException("Endpoint cannot be null or empty", nameof(endpoint));
            }

            if (string.IsNullOrWhiteSpace(httpMethod)) {
                throw new ArgumentException("HTTP method cannot be null or empty", nameof(httpMethod));
            }

            var usage = new Usage {
                UserId = userId,
                Endpoint = endpoint,
                HttpMethod = httpMethod,
                QueryId = queryId ?? string.Empty,
                RequestTime = DateTime.UtcNow,
                DurationMs = durationMs,
                StatusCode = statusCode,
                IsSuccess = statusCode >= 200 && statusCode < 300,
                CallerIp = callerIp,
                UserAgent = userAgent
            };

            try {
                var recordedUsage = await _usageRepository.RecordUsageAsync(usage);
                _logger.LogDebug("Recorded usage for user {UserId} on endpoint {Endpoint}", userId, endpoint);
                return recordedUsage;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to record usage for user {UserId} on {Endpoint}", userId, endpoint);
                throw new InvalidOperationException($"Failed to record usage for user {userId} on {nameof(endpoint)} {endpoint}", ex);
            }
        }

        /// <summary>
        /// Records a successful usage event
        /// </summary>
        public async Task<Usage> RecordSuccessAsync(
            string userId,
            string endpoint,
            string httpMethod,
            string? queryId = null,
            long durationMs = 0,
            string? callerIp = null,
            string? userAgent = null) {
            return await RecordUsageAsync(
                userId: userId,
                endpoint: endpoint,
                httpMethod: httpMethod,
                queryId: queryId,
                durationMs: durationMs,
                callerIp: callerIp,
                userAgent: userAgent);
        }

        /// <summary>
        /// Records a failed usage event
        /// </summary>
        public async Task<Usage> RecordFailureAsync(
            string userId,
            string endpoint,
            string httpMethod,
            int statusCode,
            string? queryId = null,
            long durationMs = 0,
            string? callerIp = null,
            string? userAgent = null) {
            return await RecordUsageAsync(
                userId: userId,
                endpoint: endpoint,
                httpMethod: httpMethod,
                queryId: queryId,
                durationMs: durationMs,
                statusCode: statusCode,
                callerIp: callerIp,
                userAgent: userAgent);
        }

        /// <summary>
        /// Gets usage statistics for a user within a date range
        /// </summary>
        public async Task<Usage[]> GetUsageByDateRangeAsync(
            string userId,
            DateTime startDate,
            DateTime endDate) {
            if (string.IsNullOrWhiteSpace(userId)) {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            if (startDate > endDate) {
                throw new ArgumentException("Start date cannot be after end date");
            }

            try {
                var usageRecords = await _usageRepository.GetUsageByUserAndDateRangeAsync(userId, startDate, endDate);
                _logger.LogDebug("Retrieved {Count} usage records for user {UserId} between {StartDate} and {EndDate}",
                    usageRecords.Length, userId, startDate, endDate);
                return usageRecords;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get usage for user {UserId} between {StartDate} and {EndDate}",
                    userId, startDate, endDate);
                throw new InvalidOperationException($"Failed to get usage for user {userId} between {startDate} and {endDate}", ex);
            }
        }
    }
}
