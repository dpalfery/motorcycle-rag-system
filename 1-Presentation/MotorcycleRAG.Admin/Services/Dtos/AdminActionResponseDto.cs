namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Admin-app DTO for action responses that return the latest row state.
/// </summary>
internal sealed class AdminActionResponseDto {
    public UserManagementRowDto Row { get; set; } = new();
}