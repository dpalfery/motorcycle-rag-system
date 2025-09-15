using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.Shared.Configuration
{
    public static class JsonSerializationConfiguration
    {
        public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

        public static JsonSerializerOptions PrettyPrintOptions { get; } = CreatePrettyPrintOptions();

        public static JsonSerializerOptions MinimalOptions { get; } = CreateMinimalOptions();

        public static JsonSerializerOptions GetEnvironmentOptions(bool isDevelopment)
        {
            return isDevelopment ? PrettyPrintOptions : DefaultOptions;
        }

        private static JsonSerializerOptions CreateDefaultOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };

            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return options;
        }

        private static JsonSerializerOptions CreatePrettyPrintOptions()
        {
            var opts = CreateDefaultOptions();
            opts.WriteIndented = true;
            return opts;
        }

        private static JsonSerializerOptions CreateMinimalOptions()
        {
            var opts = CreateDefaultOptions();
            opts.WriteIndented = false;
            return opts;
        }
    }
}
