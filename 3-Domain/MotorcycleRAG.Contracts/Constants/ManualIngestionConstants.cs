namespace MotorcycleRAG.Contracts.Constants;

public static class ManualIngestionConstants
{
    public static class Stages
    {
        public const string Source = "01-source";
        public const string Canonicalize = "02-canonicalize";
        public const string Chunk = "03-chunk";
        public const string ExtractGraph = "04-extract-graph";
        public const string Vectorize = "05-vectorize";
        public const string Index = "06-index";
        public const string Complete = "07-complete";
    }

    public static class ArtifactTypes
    {
        public const string Chunks = "chunks";
        public const string Entities = "entities";
        public const string Relationships = "relationships";
        public const string Vectors = "vectors";
    }
}
