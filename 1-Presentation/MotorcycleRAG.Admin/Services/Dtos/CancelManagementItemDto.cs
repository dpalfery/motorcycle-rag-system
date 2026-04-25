namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Admin-app DTO shared by cancel actions for requests and managed users.
/// </summary>
internal sealed class CancelManagementItemDto {
    public string ExpectedRowVersion { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}