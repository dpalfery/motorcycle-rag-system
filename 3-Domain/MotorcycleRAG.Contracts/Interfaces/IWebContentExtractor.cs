using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Extracts relevant text from trusted-source HTML.
/// </summary>
public interface IWebContentExtractor
{
    /// <summary>
    /// Extracts text relevant to <paramref name="searchTerm"/> from source HTML.
    /// </summary>
    string Extract(string htmlContent, string searchTerm, TrustedSourceOptions source);
}
