using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Service interface for notifying approvers about new onboarding requests.
    /// </summary>
    public interface IApproverNotificationService {
        /// <summary>
        /// Sends a notification for a newly submitted access request.
        /// </summary>
        Task SendAccessRequestSubmittedAsync(PublicAccessRequestResponse accessRequest, string approverAddress);
    }
}