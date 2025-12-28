using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MotorcycleRAG.API.Authorization;

/// <summary>
/// Authorization handler for role-based requirements
/// </summary>
public class RoleRequirementHandler : IAuthorizationHandler
{
    private readonly ILogger<RoleRequirementHandler> _logger;

    public RoleRequirementHandler(ILogger<RoleRequirementHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        // Find all role requirements in the context
        var pendingRequirements = context.PendingRequirements
            .OfType<RoleRequirement>()
            .ToList();

        if (!pendingRequirements.Any())
        {
            return Task.CompletedTask;
        }

        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated ?? false)
        {
            _logger.LogWarning("Unauthenticated user attempted to access resource requiring roles");
            return Task.CompletedTask;
        }

        // Check each role requirement
        foreach (var requirement in pendingRequirements)
        {
            var hasRoleClaim = context.User.HasClaim(c =>
                c.Type == ClaimTypes.Role &&
                c.Value == requirement.RequiredRole);

            if (hasRoleClaim)
            {
                _logger.LogDebug("User authorized for role: {Role}", requirement.RequiredRole);
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("User lacks required role: {Role}. Available roles: {AvailableRoles}",
                    requirement.RequiredRole,
                    string.Join(", ", context.User.FindAll(ClaimTypes.Role).Select(c => c.Value)));
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Role-based authorization requirement
/// </summary>
public class RoleRequirement : IAuthorizationRequirement
{
    public string RequiredRole { get; }

    public RoleRequirement(string requiredRole)
    {
        RequiredRole = requiredRole ?? throw new ArgumentNullException(nameof(requiredRole));
    }
}
