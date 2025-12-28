using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.API.Services;

/// <summary>
/// Implementation of current user service that resolves user information from HTTP context claims
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CurrentUserService> _logger;

    /// <summary>
    /// Initializes a new instance of the CurrentUserService
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor</param>
    /// <param name="logger">Logger</param>
    public CurrentUserService(IHttpContextAccessor httpContextAccessor, ILogger<CurrentUserService> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the current user's ID from claims
    /// </summary>
    public string? UserId => GetClaimValue(ClaimTypes.NameIdentifier) ?? GetClaimValue("sub");

    /// <summary>
    /// Gets the current user's email from claims
    /// </summary>
    public string? Email => GetClaimValue(ClaimTypes.Email) ?? GetClaimValue("email");

    /// <summary>
    /// Gets the current user's display name from claims
    /// </summary>
    public string? DisplayName => GetClaimValue(ClaimTypes.Name) ?? GetClaimValue("name");

    /// <summary>
    /// Gets the current user's first name from claims
    /// </summary>
    public string? FirstName => GetClaimValue(ClaimTypes.GivenName) ?? GetClaimValue("given_name");

    /// <summary>
    /// Gets the current user's last name from claims
    /// </summary>
    public string? LastName => GetClaimValue(ClaimTypes.Surname) ?? GetClaimValue("family_name");

    /// <summary>
    /// Gets the authentication provider from claims
    /// </summary>
    public string? AuthProvider => GetClaimValue("idp");

    /// <summary>
    /// Gets the provider-specific user ID from claims
    /// </summary>
    public string? ProviderUserId => GetClaimValue("oid") ?? GetClaimValue("sub");

    /// <summary>
    /// Checks if the current user is authenticated
    /// </summary>
    public bool IsAuthenticated
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.Identity?.IsAuthenticated ?? false;
        }
    }

    /// <summary>
    /// Checks if the current user has a specific role
    /// </summary>
    /// <param name="role">Role to check</param>
    /// <returns>True if user has the role, false otherwise</returns>
    public bool IsInRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            _logger.LogWarning("Attempted to check role {Role} but HTTP context is null", role);
            return false;
        }

        // Check both ClaimTypes.Role and "roles" claim (Azure AD/B2C uses "roles")
        var hasRole = user.IsInRole(role) ||
            user.HasClaim(c => c.Type == "roles" && c.Value == role);

        if (!hasRole)
        {
            _logger.LogDebug("User {UserId} does not have role {Role}", UserId, role);
        }

        return hasRole;
    }

    /// <summary>
    /// Gets all claims for the current user
    /// </summary>
    /// <returns>Collection of claims</returns>
    public IEnumerable<Claim> GetClaims()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.Claims ?? Enumerable.Empty<Claim>();
    }

    /// <summary>
    /// Helper method to get a claim value by type
    /// </summary>
    /// <param name="claimType">Type of claim to retrieve</param>
    /// <returns>Claim value if found, null otherwise</returns>
    private string? GetClaimValue(string claimType)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            return null;
        }

        var claim = user.FindFirst(claimType);
        return claim?.Value;
    }
}
