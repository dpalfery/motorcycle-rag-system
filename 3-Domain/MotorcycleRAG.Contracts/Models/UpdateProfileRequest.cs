namespace MotorcycleRAG.Domain.Models;

/// <summary>
/// Update profile request model
/// </summary>
public class UpdateProfileRequest
{
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
