namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Request body for updating the enabled state of a user.
/// </summary>
internal class SetUserEnabledRequestDto
{
    public bool IsEnabled { get; set; }
}
