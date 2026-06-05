namespace MotorcycleRAG.Admin.Services;

internal static class LocalProcessorDefaults {
    internal const string DefaultEndpoint = "http://localhost:8100";

    /// <summary>
    /// Platform-specific command to start the local Python processor.
    /// Windows uses the Python Launcher (py -3); macOS uses python3 directly.
    /// </summary>
#if MACCATALYST
    internal const string DefaultStartCommand = "python3 src/main.py";
#else
    internal const string DefaultStartCommand = "py -3 src/main.py";
#endif
    internal const int DefaultPdfChunkerMaxTokens = 512;
    internal const int DefaultCsvChunkMaxTokens = 512;
    internal const string DefaultPdfChunkerTokenizer = "BAAI/bge-small-en-v1.5";

    internal static string? TryFindWorkingDirectory() {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 10 && current != null; depth++, current = current.Parent) {
            var candidate = Path.Combine(current.FullName, "2-Application", "local-processing-service");
            if (File.Exists(Path.Combine(candidate, "src", "main.py"))) {
                return candidate;
            }
        }

        return null;
    }
}
