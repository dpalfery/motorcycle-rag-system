namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// Result of validation operation
/// </summary>
public class ValidationResult
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public bool IsValid { get; set; }
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();
    public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

    // Internal methods for modification
    internal void AddError(string error) => _errors.Add(error);
    internal void AddWarning(string warning) => _warnings.Add(warning);
}
