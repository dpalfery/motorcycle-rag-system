using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.DbSetup;

public class InteractivePrompter
{
    private readonly ILogger<InteractivePrompter> _logger;

    public InteractivePrompter(ILogger<InteractivePrompter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool PromptForConfirmation(string message, bool defaultYes = false)
    {
        if (IsNonInteractive())
        {
            _logger.LogWarning("Non-interactive mode: Cannot prompt for confirmation. Use --force to override.");
            return false;
        }

        Console.WriteLine(message);

        var yesResponse = defaultYes ? "Y" : "y";
        var noResponse = defaultYes ? "n" : "N";
        var defaultText = defaultYes ? $" (Y/n)" : " (y/N)";

        Console.Write($" {defaultText}: ");

        var response = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(response))
        {
            return defaultYes;
        }

        return response == "y" || response == "yes";
    }

    public string? PromptForInput(string message, string? defaultValue = null, bool required = false)
    {
        if (IsNonInteractive())
        {
            if (required && string.IsNullOrEmpty(defaultValue))
            {
                throw new InvalidOperationException($"Non-interactive mode: Required input '{message}' not provided and no default value available.");
            }

            _logger.LogInformation("Non-interactive mode: Using default value for '{Message}': {DefaultValue}", message, defaultValue ?? "none");
            return defaultValue;
        }

        Console.Write(message);

        if (!string.IsNullOrEmpty(defaultValue))
        {
            Console.Write($" (default: {defaultValue})");
        }

        Console.Write(": ");

        var response = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(response))
        {
            if (required && string.IsNullOrEmpty(defaultValue))
            {
                _logger.LogError("Required input '{Message}' not provided", message);
                throw new InvalidOperationException($"Required input '{message}' not provided.");
            }

            return defaultValue;
        }

        return response;
    }

    public string PromptForPassword(string message, bool confirm = true)
    {
        if (IsNonInteractive())
        {
            throw new InvalidOperationException("Non-interactive mode: Cannot prompt for password. Provide --sa-password or set MOTORCYCLERAG_DB_SA_PASSWORD environment variable.");
        }

        Console.Write($"{message}: ");

        var password = ReadPasswordFromConsole();

        if (confirm)
        {
            Console.WriteLine();
            Console.Write("Confirm password: ");
            var confirmPassword = ReadPasswordFromConsole();

            if (password != confirmPassword)
            {
                throw new InvalidOperationException("Passwords do not match.");
            }
        }

        Console.WriteLine();
        return password;
    }

    public bool PromptForDestructiveAction(string actionDescription, bool force = false)
    {
        if (force)
        {
            _logger.LogWarning("Force mode enabled: Skipping confirmation for destructive action: {ActionDescription}", actionDescription);
            return true;
        }

        var message = $"WARNING: This action will {actionDescription}. This cannot be undone.\nDo you want to continue? (y/N): ";

        return PromptForConfirmation(message, defaultYes: false);
    }

    public void ShowProgress(string message)
    {
        if (IsNonInteractive())
        {
            _logger.LogInformation("{Message}", message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    public void ShowError(string message)
    {
        if (IsNonInteractive())
        {
            _logger.LogError("{Message}", message);
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
        }
    }

    public void ShowWarning(string message)
    {
        if (IsNonInteractive())
        {
            _logger.LogWarning("{Message}", message);
        }
        else
        {
            Console.WriteLine($"Warning: {message}");
        }
    }

    public void ShowSuccess(string message)
    {
        if (IsNonInteractive())
        {
            _logger.LogInformation("{Message}", message);
        }
        else
        {
            Console.WriteLine($"✓ {message}");
        }
    }

    private static bool IsNonInteractive()
    {
        // Check if we're running in a non-interactive environment
        return Console.IsInputRedirected || Console.IsOutputRedirected || Environment.GetCommandLineArgs().Any(arg => arg.Contains("--non-interactive"));
    }

    private static string ReadPasswordFromConsole()
    {
        var password = string.Empty;
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password = password.Substring(0, password.Length - 1);
                Console.Write("\b \b");
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            else if (key.KeyChar != '\u0000')
            {
                password += key.KeyChar;
                Console.Write("*");
            }
        }
        while (key.Key != ConsoleKey.Enter);

        return password;
    }
}
