namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Implementation of ISettingsService wrapping MAUI's Preferences and SecureStorage APIs.
/// Provides a testable abstraction for application settings and secure credential storage.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection")]
internal class SettingsService : ISettingsService
{
    /// <summary>
    /// Retrieves a non-sensitive setting value from Preferences.
    /// </summary>
    public async Task<string> GetAsync(string key, string defaultValue = "")
    {
        // Preferences.Get is synchronous on all platforms, but we wrap in Task for consistency
        return await Task.FromResult(
            Preferences.Get(key, defaultValue)
        );
    }

    /// <summary>
    /// Stores a non-sensitive setting value in Preferences.
    /// </summary>
    public async Task SetAsync(string key, string value)
    {
        await Task.Run(() => Preferences.Set(key, value));
    }

    /// <summary>
    /// Retrieves a sensitive setting value from SecureStorage.
    /// SecureStorage is async and uses platform-native secure storage mechanisms:
    /// - iOS: Keychain
    /// - Android: Keystore / EncryptedSharedPreferences
    /// - Windows: Data Protection API (DPAPI)
    /// </summary>
    public async Task<string> GetSecureAsync(string key)
    {
        try
        {
            var value = await SecureStorage.GetAsync(key);
            return value ?? string.Empty;
        }
        catch (Exception ex)
        {
            // Log but don't throw - returning empty string is a safe default
            System.Diagnostics.Debug.WriteLine($"Error retrieving secure setting {key}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Stores a sensitive setting value in SecureStorage.
    /// </summary>
    public async Task SetSecureAsync(string key, string value)
    {
        try
        {
            await SecureStorage.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            // Log and rethrow - caller needs to know if secure storage failed
            System.Diagnostics.Debug.WriteLine($"Error storing secure setting {key}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Removes a non-sensitive setting value from Preferences.
    /// </summary>
    public async Task RemoveAsync(string key)
    {
        await Task.Run(() => Preferences.Remove(key));
    }

    /// <summary>
    /// Removes a sensitive setting value from SecureStorage.
    /// </summary>
    public async Task RemoveSecureAsync(string key)
    {
        try
        {
            SecureStorage.Remove(key);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error removing secure setting {key}: {ex.Message}");
            throw;
        }
    }
}
