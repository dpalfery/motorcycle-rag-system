namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Issues short-lived tokens that allow the local processor to download an ingestion
/// source blob through the API without direct storage credentials.
/// </summary>
public interface IIngestionSourceAccessTokenService
{
    /// <summary>Creates a token bound to the given upload and document type.</summary>
    string CreateToken(string uploadId, string documentType, TimeSpan? lifetime = null);

    /// <summary>Returns true when the token is valid for the given upload and document type.</summary>
    bool IsValid(string token, string uploadId, string documentType);
}
