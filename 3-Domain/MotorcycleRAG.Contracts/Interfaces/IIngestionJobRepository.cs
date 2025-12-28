using MotorcycleRAG.Contracts.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for managing ingestion job records
/// </summary>
public interface IIngestionJobRepository
{
    /// <summary>
    /// Creates a new ingestion job record
    /// </summary>
    /// <param name="job">The ingestion job to create</param>
    /// <returns>The created ingestion job with assigned ID</returns>
    Task<IngestionJob> CreateAsync(IngestionJob job);

    /// <summary>
    /// Gets an ingestion job by its ID
    /// </summary>
    /// <param name="id">The job ID</param>
    /// <returns>The ingestion job, or null if not found</returns>
    Task<IngestionJob?> GetByIdAsync(long id);

    /// <summary>
    /// Gets an ingestion job by its unique job identifier
    /// </summary>
    /// <param name="jobId">The unique job identifier</param>
    /// <returns>The ingestion job, or null if not found</returns>
    Task<IngestionJob?> GetByJobIdAsync(string jobId);

    /// <summary>
    /// Updates an existing ingestion job
    /// </summary>
    /// <param name="job">The ingestion job to update</param>
    /// <returns>The updated ingestion job</returns>
    Task<IngestionJob> UpdateAsync(IngestionJob job);

    /// <summary>
    /// Updates the status of an ingestion job
    /// </summary>
    /// <param name="jobId">The unique job identifier</param>
    /// <param name="status">The new status</param>
    /// <param name="endTime">Optional end time to set</param>
    /// <param name="errorMessage">Optional error message</param>
    /// <returns>True if update was successful</returns>
    Task<bool> UpdateStatusAsync(string jobId, IngestionJobStatus status, DateTime? endTime = null, string? errorMessage = null);

    /// <summary>
    /// Updates the metrics of an ingestion job
    /// </summary>
    /// <param name="jobId">The unique job identifier</param>
    /// <param name="totalRecordsProcessed">Total records processed</param>
    /// <param name="recordsIndexed">Records successfully indexed</param>
    /// <param name="recordsFailed">Records that failed</param>
    /// <param name="recordsWithWarnings">Records with warnings</param>
    /// <returns>True if update was successful</returns>
    Task<bool> UpdateMetricsAsync(
        string jobId,
        int totalRecordsProcessed,
        int recordsIndexed,
        int recordsFailed = 0,
        int recordsWithWarnings = 0);

    /// <summary>
    /// Adds an error to an ingestion job
    /// </summary>
    /// <param name="jobId">The unique job identifier</param>
    /// <param name="errorMessage">The error message to add</param>
    /// <returns>True if error was added successfully</returns>
    Task<bool> AddErrorAsync(string jobId, string errorMessage);

    /// <summary>
    /// Gets ingestion jobs by status
    /// </summary>
    /// <param name="status">The status to filter by</param>
    /// <param name="limit">Maximum number of jobs to return</param>
    /// <returns>List of ingestion jobs with the specified status</returns>
    Task<IngestionJob[]> GetByStatusAsync(IngestionJobStatus status, int limit = 100);

    /// <summary>
    /// Gets ingestion jobs by job type
    /// </summary>
    /// <param name="jobType">The job type to filter by</param>
    /// <param name="limit">Maximum number of jobs to return</param>
    /// <returns>List of ingestion jobs of the specified type</returns>
    Task<IngestionJob[]> GetByJobTypeAsync(IngestionJobType jobType, int limit = 100);

    /// <summary>
    /// Gets recent ingestion jobs
    /// </summary>
    /// <param name="limit">Maximum number of jobs to return</param>
    /// <returns>List of recent ingestion jobs</returns>
    Task<IngestionJob[]> GetRecentJobsAsync(int limit = 50);

    /// <summary>
    /// Gets ingestion jobs for a specific user
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="limit">Maximum number of jobs to return</param>
    /// <returns>List of ingestion jobs for the user</returns>
    Task<IngestionJob[]> GetByUserIdAsync(string userId, int limit = 100);

    /// <summary>
    /// Gets ingestion jobs within a date range
    /// </summary>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <returns>List of ingestion jobs within the date range</returns>
    Task<IngestionJob[]> GetByDateRangeAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// Deletes an ingestion job by its ID
    /// </summary>
    /// <param name="id">The job ID</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteAsync(long id);
}
