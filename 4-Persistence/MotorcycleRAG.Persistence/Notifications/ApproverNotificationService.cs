using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Persistence.Notifications;

/// <summary>
/// Emits structured approver-notification events for newly submitted access requests.
/// </summary>
public class ApproverNotificationService : IApproverNotificationService
{
    private readonly ILogger<ApproverNotificationService> _logger;

    public ApproverNotificationService(ILogger<ApproverNotificationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends a notification for a newly submitted access request.
    /// </summary>
    public Task SendAccessRequestSubmittedAsync(PublicAccessRequestResponse accessRequest, string approverAddress)
    {
        ArgumentNullException.ThrowIfNull(accessRequest);

        if (string.IsNullOrWhiteSpace(approverAddress))
        {
            throw new ArgumentException("Approver address cannot be null or empty", nameof(approverAddress));
        }

        _logger.LogInformation(
            "Approver notification recorded for access request {RequestId} to {ApproverAddress} for {Provider}/{Email} with correlation {CorrelationId}",
            accessRequest.RequestId,
            approverAddress,
            accessRequest.Provider,
            accessRequest.Email,
            accessRequest.CorrelationId);

        return Task.CompletedTask;
    }
}