namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Determines which document-processing backend the ingestion pipeline uses.
/// </summary>
public enum ProcessingMode
{
    /// <summary>
    /// Use local Python FastAPI service (default).
    /// </summary>
    Local = 0,

    /// <summary>
    /// Use Microsoft Fabric pipeline (legacy fallback).
    /// </summary>
    Fabric = 1
}
