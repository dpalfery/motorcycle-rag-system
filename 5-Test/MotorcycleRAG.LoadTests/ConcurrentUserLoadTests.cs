using NBomber.CSharp;
using NBomber.Http.CSharp;
using NBomber.Contracts.Stats;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Security.Cryptography;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.LoadTests;

/// <summary>
/// Load tests for concurrent user scenarios using NBomber framework.
/// Tests system performance under various load conditions.
/// </summary>
public class ConcurrentUserLoadTests
{
    private static readonly string[] SimpleQueries =
    [
        "What are the specifications for Honda CBR600RR?",
        "Tell me about Yamaha R1 engine specs",
        "What is the top speed of Kawasaki Ninja ZX-10R?",
        "How much does a BMW S1000RR weigh?",
        "What type of engine does Ducati Panigale V4 have?"
    ];

    private static readonly string[] ComplexQueries =
    [
        "Compare the performance between Honda CBR1000RR and Yamaha R1",
        "What maintenance procedures are required for Ducati Panigale V4?",
        "How do I adjust the suspension on a BMW S1000RR?",
        "What are the differences between Kawasaki ZX-10R and ZX-6R?",
        "Explain the electronic systems on modern superbikes"
    ];

    private static readonly string[] MixedSimpleQueries =
    [
        "Honda CBR600RR specs",
        "Yamaha R1 top speed",
        "BMW S1000RR weight"
    ];

    private static readonly string[] MixedComplexQueries =
    [
        "Compare Honda CBR1000RR vs Yamaha R1 performance",
        "Maintenance schedule for Ducati Panigale V4",
        "Suspension tuning guide for track riding"
    ];

    private static readonly string[] StabilityQueries =
    [
        "Honda motorcycle specifications",
        "Yamaha performance data",
        "Kawasaki maintenance info",
        "BMW technical details",
        "Ducati engine specs"
    ];

    private readonly IConfiguration _configuration;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly HttpClient _httpClient;

    public ConcurrentUserLoadTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Load.json")
            .AddEnvironmentVariables()
            .Build();

        _baseUrl = _configuration["LoadTest:BaseUrl"] ?? "https://localhost:7001";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        _httpClient = Http.CreateDefaultClient();
    }

    [Fact]
    public void LoadTest_SimpleQueries_HandlesTargetConcurrentUsers()
    {
        var scenario = Scenario.Create("simple_queries", async context =>
        {
            var query = PickRandom(SimpleQueries);
            var request = new MotorcycleQueryRequest
            {
                Query = query,
                UserId = $"load-test-user-{context.ScenarioInfo.InstanceId}",
                Context = new QueryContext
                {
                    SessionId = context.ScenarioInfo.InstanceId,
                    Timestamp = DateTime.UtcNow
                }
            };

            var req = Http.CreateRequest("POST", $"{_baseUrl}/api/motorcycle/query")
                          .WithJsonBody(request, _jsonOptions);

            return await Http.Send(_httpClient, req);
        })
        .WithLoadSimulations(
            Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1)),
            Simulation.KeepConstant(copies: 50, during: TimeSpan.FromMinutes(3)),
            Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-results")
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv)
            .Run();

        // Assert performance requirements
        var scnStats = stats.ScenarioStats.First(x => x.ScenarioName == "simple_queries");

        scnStats.Ok.Latency.MeanMs.Should().BeLessThan(3000); // < 3 seconds average
        scnStats.Ok.Latency.Percent95.Should().BeLessThan(5000); // < 5 seconds 95th percentile
        scnStats.AllOkCount.Should().BeGreaterThan(0);
        scnStats.AllFailCount.Should().BeLessThan((int)(scnStats.AllOkCount * 0.05)); // < 5% failure rate
    }

    [Fact]
    public void LoadTest_ComplexQueries_MaintainsPerformanceUnderLoad()
    {
        var scenario = Scenario.Create("complex_queries", async context =>
        {
            var query = PickRandom(ComplexQueries);
            var request = new MotorcycleQueryRequest
            {
                Query = query,
                UserId = $"complex-user-{context.ScenarioInfo.InstanceId}",
                Preferences = new SearchPreferences
                {
                    IncludeWebSources = true,
                    IncludePDFSources = true,
                    MaxResults = 10
                }
            };

            var req = Http.CreateRequest("POST", $"{_baseUrl}/api/motorcycle/query")
                          .WithJsonBody(request, _jsonOptions);

            return await Http.Send(_httpClient, req);
        })
        .WithLoadSimulations(
            Simulation.Inject(rate: 5, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2)),
            Simulation.KeepConstant(copies: 25, during: TimeSpan.FromMinutes(5)),
            Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-results")
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv)
            .Run();

        // Assert performance requirements for complex queries
        var scnStats = stats.ScenarioStats.First(x => x.ScenarioName == "complex_queries");

        scnStats.Ok.Latency.MeanMs.Should().BeLessThan(8000); // < 8 seconds average for complex queries
        scnStats.Ok.Latency.Percent95.Should().BeLessThan(15000); // < 15 seconds 95th percentile
        scnStats.AllFailCount.Should().BeLessThan((int)(scnStats.AllOkCount * 0.1)); // < 10% failure rate
    }

    [Fact]
    public void LoadTest_MixedWorkload_SimulatesRealisticUsage()
    {
        var simpleQueryScenario = Scenario.Create("simple_mixed", async context =>
        {
            var query = PickRandom(MixedSimpleQueries);
            var request = new MotorcycleQueryRequest
            {
                Query = query,
                UserId = $"mixed-simple-{context.ScenarioInfo.InstanceId}"
            };

            var req = Http.CreateRequest("POST", $"{_baseUrl}/api/motorcycle/query")
                          .WithJsonBody(request, _jsonOptions);
            return await Http.Send(_httpClient, req);
        })
        .WithWeight(60) // 60% of traffic
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 30, during: TimeSpan.FromMinutes(5))
        );

        var complexQueryScenario = Scenario.Create("complex_mixed", async context =>
        {
            var query = PickRandom(MixedComplexQueries);
            var request = new MotorcycleQueryRequest
            {
                Query = query,
                UserId = $"mixed-complex-{context.ScenarioInfo.InstanceId}",
                Preferences = new SearchPreferences
                {
                    IncludeWebSources = true,
                    MaxResults = 15
                }
            };

            var req = Http.CreateRequest("POST", $"{_baseUrl}/api/motorcycle/query")
                          .WithJsonBody(request, _jsonOptions);
            return await Http.Send(_httpClient, req);
        })
        .WithWeight(30) // 30% of traffic
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 15, during: TimeSpan.FromMinutes(5))
        );

        var healthCheckScenario = Scenario.Create("health_checks", async context =>
        {
            var req = Http.CreateRequest("GET", $"{_baseUrl}/api/motorcycle/health");
            return await Http.Send(_httpClient, req);
        })
        .WithWeight(10) // 10% of traffic
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 5, during: TimeSpan.FromMinutes(5))
        );

        var stats = NBomberRunner
            .RegisterScenarios(simpleQueryScenario, complexQueryScenario, healthCheckScenario)
            .WithReportFolder("load-test-results")
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv)
            .Run();

        // Assert overall system performance
        stats.ScenarioStats.Should().AllSatisfy(scenario =>
        {
            scenario.AllFailCount.Should().BeLessThan((int)(scenario.AllOkCount * 0.05)); // < 5% failure rate
        });

        // Health checks should be very fast
        var healthStats = stats.ScenarioStats.First(x => x.ScenarioName == "health_checks");
        healthStats.Ok.Latency.MeanMs.Should().BeLessThan(500); // < 500ms for health checks
    }

    [Fact]
    public void LoadTest_SpikeTraffic_HandlesTrafficSpikes()
    {
        var scenario = Scenario.Create("spike_test", async context =>
        {
            var request = new MotorcycleQueryRequest
            {
                Query = "Quick motorcycle specification lookup",
                UserId = $"spike-user-{context.ScenarioInfo.InstanceId}"
            };

            var req = Http.CreateRequest("POST", $"{_baseUrl}/api/motorcycle/query")
                          .WithJsonBody(request, _jsonOptions);
            return await Http.Send(_httpClient, req);
        })
        .WithLoadSimulations(
            // Normal load
            Simulation.KeepConstant(copies: 10, during: TimeSpan.FromMinutes(2)),
            // Spike
            Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)),
            // Recovery
            Simulation.KeepConstant(copies: 10, during: TimeSpan.FromMinutes(2))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-results")
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv)
            .Run();

        // System should handle spikes gracefully
        var scnStats = stats.ScenarioStats.First(x => x.ScenarioName == "spike_test");

        // Allow higher failure rate during spike, but should recover
        scnStats.AllFailCount.Should().BeLessThan((int)(scnStats.AllOkCount * 0.15)); // < 15% failure rate overall
        scnStats.AllOkCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LoadTest_LongRunning_MaintainsStabilityOverTime()
    {
        var scenario = Scenario.Create("stability_test", async context =>
        {
            var query = PickRandom(StabilityQueries);
            var request = new MotorcycleQueryRequest
            {
                Query = query,
                UserId = $"stability-user-{context.ScenarioInfo.InstanceId}"
            };

            var req = Http.CreateRequest("POST", $"{_baseUrl}/api/motorcycle/query")
                          .WithJsonBody(request, _jsonOptions);
            return await Http.Send(_httpClient, req);
        })
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 20, during: TimeSpan.FromMinutes(10))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-results")
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv)
            .Run();

        // Long-running stability requirements
        var scnStats = stats.ScenarioStats.First(x => x.ScenarioName == "stability_test");

        scnStats.Ok.Latency.MeanMs.Should().BeLessThan(4000); // < 4 seconds average
        scnStats.AllFailCount.Should().BeLessThan((int)(scnStats.AllOkCount * 0.02)); // < 2% failure rate
        scnStats.AllOkCount.Should().BeGreaterThan(1000); // Should handle significant volume
    }

    [Fact]
    public void LoadTest_ResourceUtilization_MonitorsSystemResources()
    {
        var scenario = Scenario.Create("resource_monitoring", async context =>
        {
            var request = new MotorcycleQueryRequest
            {
                Query = "System resource utilization test query",
                UserId = $"resource-user-{context.ScenarioInfo.InstanceId}"
            };

            var req = Http.CreateRequest("POST", $"{_baseUrl}/api/motorcycle/query")
                          .WithJsonBody(request, _jsonOptions);
            return await Http.Send(_httpClient, req);
        })
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 50, during: TimeSpan.FromMinutes(3))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-results")
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv)
            .Run();

        // Resource utilization assertions
        var scnStats = stats.ScenarioStats.First(x => x.ScenarioName == "resource_monitoring");

        // Verify throughput meets requirements
        var throughputPerSecond = scnStats.AllOkCount / stats.Duration.TotalSeconds;
        throughputPerSecond.Should().BeGreaterThan(10); // At least 10 requests per second

        // Response time consistency
        var responseTimeStdDev = scnStats.Ok.Latency.StdDev;
        responseTimeStdDev.Should().BeLessThan(2000); // Response times should be consistent
    }

    private static string PickRandom(IReadOnlyList<string> values) =>
        values[RandomNumberGenerator.GetInt32(values.Count)];
}
