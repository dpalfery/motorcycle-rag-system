using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.DbSetup;

public class EnvironmentManager
{
    private readonly ILogger<EnvironmentManager> _logger;

    public EnvironmentManager(ILogger<EnvironmentManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string? GetEnvironmentVariable(string variableName)
    {
        try
        {
            return Environment.GetEnvironmentVariable(variableName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read environment variable {VariableName}", variableName);
            return null;
        }
    }

    public void SetEnvironmentVariable(string variableName, string value, EnvironmentVariableTarget target)
    {
        try
        {
            Environment.SetEnvironmentVariable(variableName, value, target);
            _logger.LogInformation("Set environment variable {VariableName} at {Target} level", variableName, target);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set environment variable {VariableName}", variableName);
            throw;
        }
    }

    public bool TrySetSystemEnvironmentVariable(string variableName, string value, bool requireAdmin = false)
    {
        if (string.IsNullOrEmpty(variableName))
            throw new ArgumentException("Variable name cannot be null or empty", nameof(variableName));

        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty", nameof(value));

        var platform = GetCurrentPlatform();

        _logger.LogInformation("Attempting to set system environment variable {VariableName} on {Platform}", variableName, platform);

        try
        {
            switch (platform)
            {
                case Platform.Windows:
                    return TrySetWindowsEnvironmentVariable(variableName, value, requireAdmin);

                case Platform.Linux:
                case Platform.MacOS:
                    return TrySetUnixEnvironmentVariable(variableName, value);

                default:
                    _logger.LogError("Unsupported platform for environment variable persistence: {Platform}", platform);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set system environment variable {VariableName}", variableName);
            return false;
        }
    }

    public bool IsAdministrator()
    {
        var platform = GetCurrentPlatform();

        switch (platform)
        {
            case Platform.Windows:
                return IsWindowsAdministrator();

            case Platform.Linux:
            case Platform.MacOS:
                return IsUnixRoot();

            default:
                _logger.LogWarning("Cannot determine administrator status on platform: {Platform}", platform);
                return false;
        }
    }

    public bool PersistWithConsent(string variableName, string value, bool nonInteractive)
    {
        if (nonInteractive)
        {
            _logger.LogInformation("Non-interactive mode: Environment variable {VariableName} will not be persisted to system. Use process environment or manual setup.", variableName);
            return false;
        }

        Console.WriteLine($"The database password will be stored in the environment variable '{variableName}'.");
        Console.WriteLine("This requires appropriate permissions and may require administrator access.");
        Console.WriteLine("Do you want to persist this environment variable to the system? (y/N): ");

        var response = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (response != "y" && response != "yes")
        {
            _logger.LogInformation("User declined to persist environment variable {VariableName}", variableName);
            return false;
        }

        var hasAdmin = IsAdministrator();
        if (!hasAdmin)
        {
            Console.WriteLine("Warning: You may not have administrator privileges. This operation might fail.");
        }

        var success = TrySetSystemEnvironmentVariable(variableName, value, requireAdmin: true);

        if (success)
        {
            _logger.LogInformation("Successfully persisted environment variable {VariableName}", variableName);
            Console.WriteLine($"Environment variable '{variableName}' has been set successfully.");
        }
        else
        {
            _logger.LogWarning("Failed to persist environment variable {VariableName}", variableName);
            Console.WriteLine($"Failed to set environment variable '{variableName}'.");
            Console.WriteLine("You can manually set it using:");
            PrintManualInstructions(variableName, value);
        }

        return success;
    }

    private bool TrySetWindowsEnvironmentVariable(string variableName, string value, bool requireAdmin)
    {
        try
        {
            // Try user-level first (doesn't require admin)
            Environment.SetEnvironmentVariable(variableName, value, EnvironmentVariableTarget.User);
            _logger.LogInformation("Set user-level environment variable {VariableName}", variableName);

            // Verify it was set
            var verifyValue = Environment.GetEnvironmentVariable(variableName);
            if (verifyValue == value)
            {
                Console.WriteLine($"Environment variable '{variableName}' set for current user.");
                Console.WriteLine("Note: New processes may need to be restarted to see this variable.");
                return true;
            }

            // If user-level failed and admin is required, try machine-level
            if (requireAdmin && IsWindowsAdministrator())
            {
                Environment.SetEnvironmentVariable(variableName, value, EnvironmentVariableTarget.Machine);
                _logger.LogInformation("Set machine-level environment variable {VariableName}", variableName);

                verifyValue = Environment.GetEnvironmentVariable(variableName);
                if (verifyValue == value)
                {
                    Console.WriteLine($"Environment variable '{variableName}' set at machine level.");
                    Console.WriteLine("Note: You may need to log out and log back in for all processes to see this variable.");
                    return true;
                }
            }

            return false;
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning(ex, "Permission denied setting environment variable {VariableName}", variableName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error setting environment variable {VariableName}", variableName);
            return false;
        }
    }

    private bool TrySetUnixEnvironmentVariable(string variableName, string value)
    {
        try
        {
            // On Unix systems, we can only set process-level variables
            // For persistence, we need to modify shell profile files
            Environment.SetEnvironmentVariable(variableName, value, EnvironmentVariableTarget.Process);

            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var shellProfile = GetShellProfilePath();

            if (!string.IsNullOrEmpty(shellProfile))
            {
                var exportLine = $"export {variableName}='{value.Replace("'", "'\"'\"'")}'";
                var fullProfilePath = Path.Combine(homeDir, shellProfile);

                if (File.Exists(fullProfilePath))
                {
                    var existingContent = File.ReadAllText(fullProfilePath);
                    if (!existingContent.Contains(exportLine))
                    {
                        File.AppendAllText(fullProfilePath, Environment.NewLine + exportLine);
                        Console.WriteLine($"Added '{exportLine}' to {shellProfile}");
                        Console.WriteLine($"Run 'source ~/{shellProfile}' or restart your shell to load the variable.");
                    }
                }
                else
                {
                    File.WriteAllText(fullProfilePath, exportLine);
                    Console.WriteLine($"Created {shellProfile} with environment variable setup.");
                }

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set Unix environment variable {VariableName}", variableName);
            return false;
        }
    }

    private Platform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Platform.Windows;
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Platform.Linux;
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Platform.MacOS;
        else
            return Platform.Unknown;
    }

    private bool IsWindowsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private bool IsUnixRoot()
    {
        try
        {
            return geteuid() == 0; // 0 is root
        }
        catch
        {
            return false;
        }
    }

    private string? GetShellProfilePath()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL")?.ToLower() ?? "";

        if (shell.Contains("zsh"))
            return ".zshrc";
        else if (shell.Contains("bash"))
            return ".bashrc";
        else if (shell.Contains("fish"))
            return ".config/fish/config.fish";
        else
            return ".profile"; // Default fallback
    }

    private void PrintManualInstructions(string variableName, string value)
    {
        var platform = GetCurrentPlatform();

        switch (platform)
        {
            case Platform.Windows:
                Console.WriteLine($"Windows PowerShell (current user): $env:{variableName} = '{value}'");
                Console.WriteLine($"Windows PowerShell (machine): [Environment]::SetEnvironmentVariable('{variableName}', '{value}', 'Machine')");
                Console.WriteLine($"Windows Command Prompt: setx {variableName} \"{value}\"");
                break;

            case Platform.MacOS:
            case Platform.Linux:
                Console.WriteLine($"Bash/Zsh: echo 'export {variableName}=\"{value}\"' >> ~/.bashrc");
                Console.WriteLine($"Then run: source ~/.bashrc");
                break;
        }
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    private enum Platform
    {
        Windows,
        Linux,
        MacOS,
        Unknown
    }
}
