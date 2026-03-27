namespace MotorcycleRAG.Admin.Services;

internal static class LocalProcessorDefaults
{
    internal const string DefaultEndpoint = "http://localhost:8100";
    internal const string DefaultStartCommand = "py -3 src/main.py";

    internal static string? TryFindWorkingDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 10 && current != null; depth++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "2-Application", "local-processing-service");
            if (File.Exists(Path.Combine(candidate, "src", "main.py")))
            {
                return candidate;
            }
        }

        return null;
    }
}
