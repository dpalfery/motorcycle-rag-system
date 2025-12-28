using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MotorcycleRAG.API.Authorization;

/// <summary>
/// Authorization handler for resource-level authorization requirements
/// </summary>
public class ResourceAuthorizationHandler : IAuthorizationHandler
{
    private readonly ILogger<ResourceAuthorizationHandler> _logger;

    public ResourceAuthorizationHandler(ILogger<ResourceAuthorizationHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        // Find all resource authorization requirements in the context
        var pendingRequirements = context.PendingRequirements
            .OfType<ResourceAuthorizationRequirement>()
            .ToList();

        if (!pendingRequirements.Any())
        {
            return Task.CompletedTask;
        }

        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated ?? false)
        {
            _logger.LogWarning("Unauthenticated user attempted to access resource");
            return Task.CompletedTask;
        }

        // Check each resource requirement
        foreach (var requirement in pendingRequirements)
        {
            // Check if user has the required role for the resource
            var hasRequiredRole = context.User.HasClaim(c =>
                c.Type == ClaimTypes.Role &&
                requirement.RequiredRoles.Contains(c.Value));

            if (hasRequiredRole)
            {
                _logger.LogDebug("User authorized for resource: {ResourceType} with role requirement", requirement.ResourceType);
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("User lacks required roles for resource: {ResourceType}. Required: {RequiredRoles}. Available: {AvailableRoles}",
                    requirement.ResourceType,
                    string.Join(", ", requirement.RequiredRoles),
                    string.Join(", ", context.User.FindAll(ClaimTypes.Role).Select(c => c.Value)));
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Resource-level authorization requirement
/// </summary>
public class ResourceAuthorizationRequirement : IAuthorizationRequirement
{
    public string ResourceType { get; }
    public string[] RequiredRoles { get; }

    public ResourceAuthorizationRequirement(string resourceType, params string[] requiredRoles)
    {
        ResourceType = resourceType ?? throw new ArgumentNullException(nameof(resourceType));
        RequiredRoles = requiredRoles ?? throw new ArgumentNullException(nameof(requiredRoles));

        if (requiredRoles.Length == 0)
        {
            throw new ArgumentException("At least one role must be specified", nameof(requiredRoles));
        }
    }
}
