namespace MotorcycleRAG.Domain.Constants;

public static class ManualIngestionStages
{
    public const string Source = "01-source";
    public const string Canonicalize = "02-canonicalize";
    public const string Chunk = "03-chunk";
    public const string ExtractGraph = "04-extract-graph";
    public const string Vectorize = "05-vectorize";
    public const string Index = "06-index";
    public const string Complete = "07-complete";
}
