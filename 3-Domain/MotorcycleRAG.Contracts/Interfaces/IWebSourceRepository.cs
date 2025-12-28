using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models;

namespace MotorcycleRAG.Contracts.Interfaces
{
    /// <summary>
    /// Repository interface for web source management operations
    /// </summary>
    public interface IWebSourceRepository
    {
        /// <summary>
        /// Creates a new web source
        /// </summary>
        /// <param name="webSource">Web source to create</param>
        /// <returns>Created web source</returns>
        Task<WebSource> CreateWebSourceAsync(WebSource webSource);

        /// <summary>
        /// Gets a web source by ID
        /// </summary>
        /// <param name="webSourceId">Web source ID</param>
        /// <returns>Web source if found, null otherwise</returns>
        Task<WebSource?> GetWebSourceByIdAsync(int webSourceId);

        /// <summary>
        /// Gets all web sources
        /// </summary>
        /// <returns>List of all web sources</returns>
        Task<WebSource[]> GetAllWebSourcesAsync();

        /// <summary>
        /// Updates an existing web source
        /// </summary>
        /// <param name="webSource">Web source to update</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateWebSourceAsync(WebSource webSource);

        /// <summary>
        /// Deletes a web source
        /// </summary>
        /// <param name="webSourceId">Web source ID</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeleteWebSourceAsync(int webSourceId);

        /// <summary>
        /// Gets web source by URL
        /// </summary>
        /// <param name="url">URL to search for</param>
        /// <returns>Web source if found, null otherwise</returns>
        Task<WebSource?> GetWebSourceByUrlAsync(string url);
    }
}
