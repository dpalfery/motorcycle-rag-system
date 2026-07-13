using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MotorcycleRAG.Persistence.Tests.Infrastructure;

/// <summary>
/// Static helper methods for common test setup patterns.
/// </summary>
internal static class TestHelpers
{
    public static ILogger<T> CreateNullLogger<T>() => NullLogger<T>.Instance;
    public static IOptions<T> OptionsFor<T>(T value) where T : class =>
        Microsoft.Extensions.Options.Options.Create(value);
}
