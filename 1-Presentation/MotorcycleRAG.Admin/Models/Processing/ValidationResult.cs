namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// Result of validation operation
/// </summary>
internal class ValidationResult
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    internal bool IsValid { get; set; }
    internal IReadOnlyList<string> Errors => _errors.AsReadOnly();
    internal IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

    // Internal methods for modification
    internal void AddError(string error) => _errors.Add(error);
    internal void AddWarning(string warning) => _warnings.Add(warning);
}
