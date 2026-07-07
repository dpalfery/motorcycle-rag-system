namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Result of a validation operation.
/// </summary>
public class ModelValidationResult {
    public bool IsValid { get; }

    public IReadOnlyList<string> Errors { get; }

    private ModelValidationResult(bool isValid, List<string> errors) {
        IsValid = isValid;
        Errors = errors ?? new List<string>();
    }

    public static ModelValidationResult Success() {
        return new ModelValidationResult(true, new List<string>());
    }

    public static ModelValidationResult Failure(IEnumerable<string> errors) {
        return new ModelValidationResult(false, errors?.ToList() ?? new List<string>());
    }

    public static ModelValidationResult Failure(string error) {
        return new ModelValidationResult(false, new List<string> { error });
    }

    public string ErrorMessage => IsValid ? "Validation passed" : string.Join("; ", Errors);
}
