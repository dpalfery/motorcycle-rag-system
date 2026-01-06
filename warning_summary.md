### CA2000 (38)
Example: C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Optimization\ConnectionPoolService.cs(114,23): warning CA2000: Call System.IDisposable.Dispose on object created by 'new SocketsHttpHandler {' before all references to it are out of scope (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2000) [C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj]

### CA1515 (36)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Configuration\ServiceConfiguration.cs(18,21): warning CA1515: Because an application's API isn't typically referenced from outside the assembly, types can be made internal (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1515) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\MotorcycleRAG.API.csproj]

### CA1849 (22)
Example: C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\Pipeline\FileUploadServiceReliabilityTests.cs(210,9): warning CA1849: 'File.WriteAllText(string, string?)' synchronously blocks. Await 'File.WriteAllTextAsync(string, string?, CancellationToken)' instead. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1849) [C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\MotorcycleRAG.UnitTests.csproj]

### CA1062 (20)
Example: C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Extensions\ServiceCollectionExtensions.cs(26,48): warning CA1062: In externally visible method 'IServiceCollection ServiceCollectionExtensions.AddMotorcycleCaching(IServiceCollection services, IConfiguration configuration)', validate parameter 'configuration' is non-null before using it. If appropriate, throw an 'ArgumentNullException' when the argument is 'null'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1062) [C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj]

### CA1861 (16)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Middleware\HostHeaderValidationMiddleware.cs(40,24): warning CA1861: Prefer 'static readonly' fields over constant array arguments if the called method is called repeatedly and is not mutating the passed array (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1861) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\MotorcycleRAG.API.csproj]

### CA1501 (14)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\AppShell.xaml.cs(15,22): warning CA1501: 'AppShell' has an object hierarchy '7' levels deep within the defining module. If possible, eliminate base classes within the hierarchy to decrease its hierarchy level below '6': 'Shell, Page, VisualElement, NavigableElement, StyleableElement, Element, BindableObject'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1501) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\MotorcycleRAG.Admin.csproj::TargetFramework=net10.0-windows10.0.19041.0]

### CA1816 (12)
Example: C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\Azure\AzureOpenAIClientWrapperTests.cs(176,17): warning CA1816: Change AzureOpenAIClientWrapperTests.Dispose() to call GC.SuppressFinalize(object). This will prevent derived types that introduce a finalizer from needing to re-implement 'IDisposable' to call it. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1816) [C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\MotorcycleRAG.UnitTests.csproj]

### CA2254 (10)
Example: C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\Resilience\CorrelationService.cs(191,36): warning CA2254: The logging message template should not vary between calls to 'LoggerExtensions.LogError(ILogger, Exception?, string?, params object?[])' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2254) [C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\MotorcycleRAG.Persistence.csproj]

### CA1002 (6)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Controllers\DataPipelineController.cs(320,93): warning CA1002: Change 'List<DataPipelineRequest>' in 'DataPipelineController.ProcessBatchAsync(List<DataPipelineRequest>)' to use 'Collection<T>', 'ReadOnlyCollection<T>' or 'KeyedCollection<K,V>' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1002) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\MotorcycleRAG.API.csproj]

### CA1506 (6)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Program.cs(19,14): warning CA1506: 'Program' is coupled with '117' different types from '67' different namespaces. Rewrite or refactor the code to decrease its class coupling below '96'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1506) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\MotorcycleRAG.API.csproj]

### CA2201 (4)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\ViewModels\IngestionViewModel.cs(235,23): warning CA2201: Exception type System.Exception is not sufficiently specific (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2201) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\MotorcycleRAG.Admin.csproj::TargetFramework=net10.0-windows10.0.19041.0]

### CA1508 (4)
Example: C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Optimization\ConnectionPoolService.cs(150,13): warning CA1508: 'client' is never 'null'. Remove or refactor the condition(s) to avoid dead code. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1508) [C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj]

### CA1845 (4)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Controllers\WebSourcesAdminController.cs(341,20): warning CA1845: Use span-based 'string.Concat' and 'AsSpan' instead of 'Substring' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1845) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\MotorcycleRAG.API.csproj]

### CA2208 (2)
Example: C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Services\ToolConfigurationService.cs(97,19): warning CA2208: Method CreateToolAsync passes 'ToolId' as the paramName argument to a ArgumentException constructor. Replace this argument with one of the method's parameter names. Note that the provided parameter name should have the exact casing as declared on the method. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2208) [C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj]

### CA1865 (2)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Middleware\HostHeaderValidationMiddleware.cs(168,33): warning CA1865: Use 'string.StartsWith(char)' instead of 'string.StartsWith(string)' when you have a string with a single char (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1865) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\MotorcycleRAG.API.csproj]

### CA1862 (2)
Example: C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Agents\WebContentExtractor.cs(100,70): warning CA1862: Prefer the string comparison method overload of 'string.Contains(string)' that takes a 'StringComparison' enum value to perform a case-insensitive comparison, but keep in mind that this might cause subtle changes in behavior, so make sure to conduct thorough testing after applying the suggestion, or if culturally sensitive comparison is not required, consider using 'StringComparison.OrdinalIgnoreCase' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1862) [C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj]

### CA1825 (2)
Example: C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\Azure\AzureSearchClientWrapperTests.cs(40,32): warning CA1825: Avoid unnecessary zero-length array allocations.  Use Array.Empty<SearchResult>() instead. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1825) [C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\MotorcycleRAG.UnitTests.csproj]

### CA1829 (2)
Example: C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\DataProcessing\MotorcycleCSVProcessor.cs(125,37): warning CA1829: Use the "Length" property instead of Enumerable.Count() (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1829) [C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\MotorcycleRAG.Persistence.csproj]

### CA5392 (2)
Example: C:\Users\dave\.nuget\packages\microsoft.windowsappsdk\1.7.250909003\include\UndockedRegFreeWinRT-AutoInitializer.cs(18,36): warning CA5392: The method WindowsAppRuntime_EnsureIsLoaded didn't use DefaultDllImportSearchPaths attribute for P/Invokes. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca5392) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\MotorcycleRAG.Admin.csproj::TargetFramework=net10.0-windows10.0.19041.0]

### CA1819 (2)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\Processing\OnnxEmbeddingService.cs(14,20): warning CA1819: Properties should not return arrays (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1819) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\MotorcycleRAG.Admin.csproj::TargetFramework=net10.0-windows10.0.19041.0]

### CA1513 (2)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\Processing\OnnxEmbeddingService.cs(59,9): warning CA1513: Use 'ObjectDisposedException.ThrowIf' instead of explicitly throwing a new exception instance (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1513) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\MotorcycleRAG.Admin.csproj::TargetFramework=net10.0-windows10.0.19041.0]

### CA1510 (2)
Example: C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Services\PlanPolicyService.cs(42,13): warning CA1510: Use 'ArgumentNullException.ThrowIfNull' instead of explicitly throwing a new exception instance (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1510) [C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj]

### CA1054 (2)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\Utilities\UrlValidator.cs(21,42): warning CA1054: Change the type of parameter 'url' of method 'UrlValidator.IsValidUrl(string, [bool])' from 'string' to 'System.Uri', or provide an overload to 'UrlValidator.IsValidUrl(string, [bool])' that allows 'url' to be passed as a 'System.Uri' object (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1054) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\MotorcycleRAG.Admin.csproj::TargetFramework=net10.0-windows10.0.19041.0]

### CA1052 (2)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Program.cs(19,14): warning CA1052: Type 'Program' is a static holder type but is neither static nor NotInheritable (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1052) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\MotorcycleRAG.API.csproj]

### CA1045 (2)
Example: C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\ViewModels\IngestionViewModel.cs(391,41): warning CA1045: Consider a design that does not require that 'backingStore' be a reference parameter (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1045) [C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.Admin\MotorcycleRAG.Admin.csproj::TargetFramework=net10.0-windows10.0.19041.0]

### CA1847 (2)
Example: C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\DataProcessing\MotorcyclePDFProcessorTests.cs(695,113): warning CA1847: Use 'string.Contains(char)' instead of 'string.Contains(string)' when searching for a single character (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1847) [C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\MotorcycleRAG.UnitTests.csproj]

### CA5394 (2)
Example: C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\Azure\AzureOpenAIClientWrapper.cs(132,66): warning CA5394: Random is an insecure random number generator. Use cryptographically secure random number generators when randomness is required for security. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca5394) [C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\MotorcycleRAG.Persistence.csproj]

