namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Standard admin action response returning the latest management row state.
/// </summary>
public class AdminActionResponse {
    public UserManagementRow Row { get; set; } = new();
}