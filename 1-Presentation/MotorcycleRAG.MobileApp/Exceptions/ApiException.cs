using System;
using System.Collections.Generic;

namespace MotorcycleRAG.MobileApp.Exceptions;

public class ApiException : Exception
{
    public int? StatusCode { get; set; }
    public string? CorrelationId { get; set; }

    public ApiException(string message) : base(message) { }
    public ApiException(string message, Exception innerException) : base(message, innerException) { }
}

public class AuthenticationException : ApiException
{
    public AuthenticationException(string message) : base(message) { }
}

public class RateLimitException : ApiException
{
    public DateTime ResetAt { get; set; }
    public RateLimitException(string message, DateTime resetAt) : base(message)
    {
        ResetAt = resetAt;
    }
}

public class ValidationException : ApiException
{
    public Dictionary<string, string[]> Errors { get; set; } = new();
    public ValidationException(string message, Dictionary<string, string[]> errors) : base(message)
    {
        Errors = errors;
    }
}
