using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Generic interface for data processing components
/// </summary>
/// <typeparam name="T">The type of input data to process</typeparam>
public interface IDataProcessor<T>
{
    /// <summary>
    /// Process input data and prepare it for indexing
    /// </summary>
    /// <param name="input">The input data to process</param>
    /// <returns>Processing result with processed data and metadata</returns>
    Task<ProcessingResult> ProcessAsync(T input);

    /// <summary>
    /// Index processed data into the search system
    /// </summary>
    /// <param name="data">The processed data to index</param>
    /// <returns>Indexing result with status and metadata</returns>
    Task<IndexingResult> IndexAsync(ProcessedData data);
}