namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Abstraction for application settings storage.
/// This interface wraps MAUI's Preferences and SecureStorage APIs.
/// Enables testability by allowing mock implementations in unit tests.
/// </summary>
internal interface ISettingsService {
    /// <summary>
    /// Retrieves a non-sensitive setting value.
    /// </summary>
    /// <param name="key">The settings key</param>
    /// <param name="defaultValue">Default value if key not found</param>
    /// <returns>The stored value or defaultValue if not found</returns>
    Task<string> GetAsync(string key, string defaultValue = "");

    /// <summary>
    /// Stores a non-sensitive setting value.
    /// </summary>
    /// <param name="key">The settings key</param>
    /// <param name="value">The value to store</param>
    /// <returns>Task representing the store operation</returns>
    Task SetAsync(string key, string value);

    /// <summary>
    /// Retrieves a sensitive setting value (e.g., authentication token).
    /// Uses secure storage on the device.
    /// </summary>
    /// <param name="key">The settings key</param>
    /// <returns>The securely stored value or empty string if not found</returns>
    Task<string> GetSecureAsync(string key);

    /// <summary>
    /// Stores a sensitive setting value (e.g., authentication token).
    /// Uses secure storage on the device.
    /// </summary>
    /// <param name="key">The settings key</param>
    /// <param name="value">The value to store securely</param>
    /// <returns>Task representing the store operation</returns>
    Task SetSecureAsync(string key, string value);

    /// <summary>
    /// Removes a setting value.
    /// </summary>
    /// <param name="key">The settings key to remove</param>
    /// <returns>Task representing the remove operation</returns>
    Task RemoveAsync(string key);

    /// <summary>
    /// Removes a sensitive setting value.
    /// </summary>
    /// <param name="key">The settings key to remove</param>
    /// <returns>Task representing the remove operation</returns>
    Task RemoveSecureAsync(string key);
}
