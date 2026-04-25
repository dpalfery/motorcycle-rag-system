using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Repository interface for the unified admin user-management projection.
    /// </summary>
    public interface IUserManagementQueryRepository {
        /// <summary>
        /// Gets a paged list of unified management rows.
        /// </summary>
        Task<UserManagementListResponse> GetRowsAsync(UserManagementRowState? rowState, string? search, int page, int pageSize);

        /// <summary>
        /// Gets a single unified management row by its row identifier.
        /// </summary>
        Task<UserManagementRow?> GetRowByIdAsync(string rowId);
    }
}