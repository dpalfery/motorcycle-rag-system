using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services;

internal sealed class ConfigurableAdminAuthService : IAdminAuthService
{
    private readonly IConfigurationStateService _configurationStateService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly object _sync = new();
    private IAdminAuthService _innerService;
    private string _configurationFingerprint = string.Empty;

    public ConfigurableAdminAuthService(
        IConfigurationStateService configurationStateService,
        ILoggerFactory loggerFactory)
    {
        _configurationStateService = configurationStateService ?? throw new ArgumentNullException(nameof(configurationStateService));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        _innerService = CreateInnerService();
        _configurationStateService.ConfigurationChanged += OnConfigurationChanged;
    }

    public bool IsAuthenticated => CurrentService.IsAuthenticated;

    public string? UserDisplayName => CurrentService.UserDisplayName;

    public Task<bool> SignInAsync() => CurrentService.SignInAsync();

    public Task SignOutAsync() => CurrentService.SignOutAsync();

    public Task<string?> GetAccessTokenAsync() => CurrentService.GetAccessTokenAsync();

    public bool IsSignedIn() => CurrentService.IsSignedIn();

    public Task<IEnumerable<string>> GetUserRolesAsync() => CurrentService.GetUserRolesAsync();

    public Task<bool> IsAuthorizedAdminAsync() => CurrentService.IsAuthorizedAdminAsync();

    private IAdminAuthService CurrentService
    {
        get
        {
            lock (_sync)
            {
                var fingerprint = BuildFingerprint();
                if (!string.Equals(fingerprint, _configurationFingerprint, StringComparison.Ordinal))
                {
                    _innerService = CreateInnerService();
                }

                return _innerService;
            }
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            _innerService = CreateInnerService();
        }
    }

    private IAdminAuthService CreateInnerService()
    {
        var logger = _loggerFactory.CreateLogger<ConfigurableAdminAuthService>();
        _configurationFingerprint = BuildFingerprint();

        if (!_configurationStateService.IsAuthConfigured)
        {
            return new DemoAdminAuthService(_loggerFactory.CreateLogger<DemoAdminAuthService>());
        }

        var scope = AdminAuthConfigurationHelper.NormalizeAdminScope(
            _configurationStateService.AuthScope!,
            logger);

        if (!AdminAuthConfigurationHelper.IsSupportedScope(scope))
        {
            logger.LogWarning("Configured auth scope '{Scope}' is invalid. Falling back to demo auth service.", scope);
            return new DemoAdminAuthService(_loggerFactory.CreateLogger<DemoAdminAuthService>());
        }

        return new MsalAdminAuthService(
            _configurationStateService.AuthClientId!,
            _configurationStateService.AuthAuthority!,
            [scope],
            _loggerFactory.CreateLogger<MsalAdminAuthService>());
    }

    private string BuildFingerprint() =>
        string.Join("|",
            _configurationStateService.AuthClientId ?? string.Empty,
            _configurationStateService.AuthAuthority ?? string.Empty,
            _configurationStateService.AuthScope ?? string.Empty);
}
