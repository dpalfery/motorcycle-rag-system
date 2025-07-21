using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Services;

/// <summary>
/// Service for validating domain models
/// </summary>
public class ModelValidationService
{
    /// <summary>
    /// Validates a model and returns validation results
    /// </summary>
    /// <typeparam name="T">Type of model to validate</typeparam>
    /// <param name="model">Model instance to validate</param>
    /// <returns>Validation result with any errors</returns>
    public ValidationResult ValidateModel<T>(T model) where T : class
    {
        if (model == null)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "Model cannot be null" }
            };
        }

        var validationContext = new ValidationContext(model);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        
        bool isValid = Validator.TryValidateObject(model, validationContext, validationResults, true);

        return new ValidationResult
        {
            IsValid = isValid,
            Errors = validationResults.Select(vr => vr.ErrorMessage ?? "Unknown validation error").ToList()
        };
    }

    /// <summary>
    /// Validates a MotorcycleSpecification with business rules
    /// </summary>
    /// <param name="specification">Motorcycle specification to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult ValidateMotorcycleSpecification(MotorcycleSpecification specification)
    {
        var result = ValidateModel(specification);
        
        if (!result.IsValid)
            return result;

        // Additional business rule validations
        var businessErrors = new List<string>();

        // Validate engine specifications if present
        if (specification.Engine != null)
        {
            var engineValidation = ValidateModel(specification.Engine);
            if (!engineValidation.IsValid)
            {
                businessErrors.AddRange(engineValidation.Errors.Select(e => $"Engine: {e}"));
            }

            // Business rule: Displacement should match horsepower range
            if (specification.Engine.DisplacementCC > 0 && specification.Engine.Horsepower > 0)
            {
                var expectedMinHp = specification.Engine.DisplacementCC / 20; // Rough estimate
                var expectedMaxHp = specification.Engine.DisplacementCC / 5;
                
                if (specification.Engine.Horsepower < expectedMinHp * 0.5 || 
                    specification.Engine.Horsepower > expectedMaxHp * 2)
                {
                    businessErrors.Add("Engine horsepower seems inconsistent with displacement");
                }
            }
        }

        // Validate performance metrics if present
        if (specification.Performance != null)
        {
            var performanceValidation = ValidateModel(specification.Performance);
            if (!performanceValidation.IsValid)
            {
                businessErrors.AddRange(performanceValidation.Errors.Select(e => $"Performance: {e}"));
            }
        }

        // Validate safety features if present
        if (specification.Safety != null)
        {
            var safetyValidation = ValidateModel(specification.Safety);
            if (!safetyValidation.IsValid)
            {
                businessErrors.AddRange(safetyValidation.Errors.Select(e => $"Safety: {e}"));
            }
        }

        // Validate pricing if present
        if (specification.Pricing != null)
        {
            var pricingValidation = ValidateModel(specification.Pricing);
            if (!pricingValidation.IsValid)
            {
                businessErrors.AddRange(pricingValidation.Errors.Select(e => $"Pricing: {e}"));
            }

            // Business rule: Price date should not be in the future
            if (specification.Pricing.PriceDate > DateTime.UtcNow.AddDays(1))
            {
                businessErrors.Add("Price date cannot be in the future");
            }
        }

        return new ValidationResult
        {
            IsValid = businessErrors.Count == 0,
            Errors = businessErrors
        };
    }

    /// <summary>
    /// Validates a MotorcycleDocument with content rules
    /// </summary>
    /// <param name="document">Document to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult ValidateMotorcycleDocument(MotorcycleDocument document)
    {
        var result = ValidateModel(document);
        
        if (!result.IsValid)
            return result;

        var businessErrors = new List<string>();

        // Validate content length (only if basic validation passed)
        if (result.IsValid && string.IsNullOrWhiteSpace(document.Content))
        {
            businessErrors.Add("Document content cannot be empty");
        }
        else if (document.Content.Length < 10)
        {
            businessErrors.Add("Document content is too short (minimum 10 characters)");
        }
        else if (document.Content.Length > 1000000) // 1MB limit
        {
            businessErrors.Add("Document content is too large (maximum 1MB)");
        }

        // Validate vector dimensions if present
        if (document.ContentVector != null)
        {
            if (document.ContentVector.Length == 0)
            {
                businessErrors.Add("Content vector cannot be empty if provided");
            }
            else if (document.ContentVector.Length != 3072) // text-embedding-3-large dimension
            {
                businessErrors.Add("Content vector must have 3072 dimensions for text-embedding-3-large model");
            }
        }

        // Validate metadata if present
        if (document.Metadata != null)
        {
            var metadataValidation = ValidateModel(document.Metadata);
            if (!metadataValidation.IsValid)
            {
                businessErrors.AddRange(metadataValidation.Errors.Select(e => $"Metadata: {e}"));
            }
        }

        return new ValidationResult
        {
            IsValid = businessErrors.Count == 0,
            Errors = businessErrors
        };
    }

    /// <summary>
    /// Validates a query request
    /// </summary>
    /// <param name="request">Query request to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult ValidateQueryRequest(MotorcycleQueryRequest request)
    {
        var result = ValidateModel(request);
        
        if (!result.IsValid)
            return result;

        var businessErrors = new List<string>();

        // Validate query content
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            businessErrors.Add("Query cannot be empty");
        }
        else if (request.Query.Length < 3)
        {
            businessErrors.Add("Query is too short (minimum 3 characters)");
        }

        // Validate preferences if present
        if (request.Preferences != null)
        {
            var preferencesValidation = ValidateModel(request.Preferences);
            if (!preferencesValidation.IsValid)
            {
                businessErrors.AddRange(preferencesValidation.Errors.Select(e => $"Preferences: {e}"));
            }

            // Business rules for preferences
            if (request.Preferences.MaxResults <= 0)
            {
                businessErrors.Add("MaxResults must be greater than 0");
            }
            else if (request.Preferences.MaxResults > 100)
            {
                businessErrors.Add("MaxResults cannot exceed 100");
            }

            if (request.Preferences.MinRelevanceScore < 0 || request.Preferences.MinRelevanceScore > 1)
            {
                businessErrors.Add("MinRelevanceScore must be between 0 and 1");
            }
        }

        return new ValidationResult
        {
            IsValid = businessErrors.Count == 0,
            Errors = businessErrors
        };
    }
}

/// <summary>
/// Result of model validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}