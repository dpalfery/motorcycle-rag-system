using System.Text.Json;
using MotorcycleRAG.Contracts.Models;

namespace MotorcycleRAG.EndToEndTests;

/// <summary>
/// Manages test data sets for comprehensive testing scenarios.
/// Provides realistic motorcycle data for various test scenarios.
/// </summary>
public class TestDataManager
{
    private readonly string _testDataPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public TestDataManager(string testDataPath = "TestData")
    {
        _testDataPath = testDataPath;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task<List<MotorcycleSpecification>> GetTestMotorcycleSpecificationsAsync()
    {
        var specifications = new List<MotorcycleSpecification>
        {
            new()
            {
                Id = "honda-cbr600rr-2023",
                Make = "Honda",
                Model = "CBR600RR",
                Year = 2023,
                Engine = new EngineSpecification
                {
                    Type = "4-Stroke DOHC",
                    DisplacementCC = 599,
                    Configuration = "Inline-4",
                    Cylinders = 4,
                    BoreMM = 67.0f,
                    StrokeMM = 42.5f,
                    CompressionRatio = "12.2:1",
                    FuelSystem = "PGM-FI"
                },
                Performance = new PerformanceMetrics
                {
                    MaxPowerHP = 118,
                    MaxPowerRPM = 14000,
                    MaxTorqueNM = 64.5f,
                    MaxTorqueRPM = 11500,
                    TopSpeedKMH = 260,
                    Transmission = "6-Speed"
                },
                Dimensions = new DimensionSpecification
                {
                    WheelbaseMM = 1375,
                    SeatHeightMM = 820,
                    DryWeightKG = 194,
                    FuelCapacityL = 18.1f
                },
                Pricing = new PricingInformation
                {
                    MSRP = 12999,
                    Currency = "USD",
                    Market = "US"
                }
            },
            new()
            {
                Id = "yamaha-r1-2023",
                Make = "Yamaha",
                Model = "YZF-R1",
                Year = 2023,
                Engine = new EngineSpecification
                {
                    Type = "4-Stroke DOHC",
                    DisplacementCC = 998,
                    Configuration = "Inline-4",
                    Cylinders = 4,
                    BoreMM = 79.0f,
                    StrokeMM = 50.9f,
                    CompressionRatio = "13.0:1",
                    FuelSystem = "Fuel Injection"
                },
                Performance = new PerformanceMetrics
                {
                    MaxPowerHP = 200,
                    MaxPowerRPM = 13500,
                    MaxTorqueNM = 112.4f,
                    MaxTorqueRPM = 11500,
                    TopSpeedKMH = 299,
                    Transmission = "6-Speed"
                },
                Dimensions = new DimensionSpecification
                {
                    WheelbaseMM = 1405,
                    SeatHeightMM = 855,
                    DryWeightKG = 201,
                    FuelCapacityL = 17.0f
                },
                Pricing = new PricingInformation
                {
                    MSRP = 17399,
                    Currency = "USD",
                    Market = "US"
                }
            },
            new()
            {
                Id = "kawasaki-zx10r-2023",
                Make = "Kawasaki",
                Model = "Ninja ZX-10R",
                Year = 2023,
                Engine = new EngineSpecification
                {
                    Type = "4-Stroke DOHC",
                    DisplacementCC = 998,
                    Configuration = "Inline-4",
                    Cylinders = 4,
                    BoreMM = 76.0f,
                    StrokeMM = 55.0f,
                    CompressionRatio = "13.0:1",
                    FuelSystem = "DFI"
                },
                Performance = new PerformanceMetrics
                {
                    MaxPowerHP = 203,
                    MaxPowerRPM = 13200,
                    MaxTorqueNM = 114.9f,
                    MaxTorqueRPM = 11400,
                    TopSpeedKMH = 300,
                    Transmission = "6-Speed"
                },
                Dimensions = new DimensionSpecification
                {
                    WheelbaseMM = 1450,
                    SeatHeightMM = 835,
                    DryWeightKG = 207,
                    FuelCapacityL = 17.0f
                },
                Pricing = new PricingInformation
                {
                    MSRP = 16999,
                    Currency = "USD",
                    Market = "US"
                }
            }
        };

        return specifications;
    }

    public async Task<List<string>> GetTestQueriesAsync()
    {
        return new List<string>
        {
            // Simple specification queries
            "What are the specifications for Honda CBR600RR?",
            "Tell me about Yamaha R1 engine specs",
            "What is the top speed of Kawasaki Ninja ZX-10R?",
            "How much does a BMW S1000RR weigh?",
            "What type of engine does Ducati Panigale V4 have?",
            
            // Comparative queries
            "Compare the performance between Honda CBR1000RR and Yamaha R1",
            "What are the differences between Kawasaki ZX-10R and ZX-6R?",
            "Which is faster, BMW S1000RR or Ducati Panigale V4?",
            "Compare fuel efficiency between Honda CBR600RR and Yamaha R6",
            
            // Maintenance queries
            "How do I change the oil on a Kawasaki Ninja ZX-10R?",
            "What maintenance procedures are required for Ducati Panigale V4?",
            "How do I adjust the suspension on a BMW S1000RR?",
            "What is the maintenance schedule for Honda CBR600RR?",
            "How often should I replace the air filter on Yamaha R1?",
            
            // Technical queries
            "Explain the electronic systems on modern superbikes",
            "What are the safety features of BMW S1000RR?",
            "How does traction control work on Kawasaki ZX-10R?",
            "What is the difference between ABS and non-ABS braking systems?",
            
            // Complex multi-part queries
            "I'm looking for a track-focused motorcycle under $20,000 with at least 180hp",
            "What are the pros and cons of inline-4 vs V-twin engines for street riding?",
            "Which motorcycles are best for beginners who want to eventually track ride?",
            "Compare the total cost of ownership between Japanese and European superbikes"
        };
    }

    public async Task<List<TestScenario>> GetTestScenariosAsync()
    {
        return new List<TestScenario>
        {
            new()
            {
                Name = "Simple Specification Lookup",
                Description = "User queries for basic motorcycle specifications",
                Queries = new[]
                {
                    "Honda CBR600RR specifications",
                    "Yamaha R1 engine specs",
                    "BMW S1000RR weight"
                },
                ExpectedResponseTime = TimeSpan.FromSeconds(3),
                ExpectedSources = new[] { SearchSource.VectorDatabase },
                ValidationCriteria = new[]
                {
                    "Response contains motorcycle make and model",
                    "Response includes specific technical specifications",
                    "Response is factually accurate"
                }
            },
            new()
            {
                Name = "Comparative Analysis",
                Description = "User requests comparison between multiple motorcycles",
                Queries = new[]
                {
                    "Compare Honda CBR1000RR vs Yamaha R1",
                    "BMW S1000RR vs Ducati Panigale V4 performance",
                    "Kawasaki ZX-10R vs Suzuki GSX-R1000R specs"
                },
                ExpectedResponseTime = TimeSpan.FromSeconds(5),
                ExpectedSources = new[] { SearchSource.VectorDatabase, SearchSource.WebAugmentation },
                ValidationCriteria = new[]
                {
                    "Response mentions both motorcycles being compared",
                    "Response includes comparative analysis",
                    "Response highlights key differences"
                }
            },
            new()
            {
                Name = "Maintenance Procedures",
                Description = "User seeks maintenance and service information",
                Queries = new[]
                {
                    "How to change oil on Honda CBR600RR",
                    "Yamaha R1 maintenance schedule",
                    "BMW S1000RR brake service procedure"
                },
                ExpectedResponseTime = TimeSpan.FromSeconds(7),
                ExpectedSources = new[] { SearchSource.PDFManual, SearchSource.WebAugmentation },
                ValidationCriteria = new[]
                {
                    "Response includes step-by-step instructions",
                    "Response mentions required tools or parts",
                    "Response includes safety warnings if applicable"
                }
            },
            new()
            {
                Name = "Complex Technical Query",
                Description = "User asks complex technical questions requiring multiple sources",
                Queries = new[]
                {
                    "Explain electronic systems on modern superbikes",
                    "How does traction control work on track-focused motorcycles",
                    "Compare braking systems across different motorcycle manufacturers"
                },
                ExpectedResponseTime = TimeSpan.FromSeconds(10),
                ExpectedSources = new[] { SearchSource.VectorDatabase, SearchSource.WebAugmentation, SearchSource.PDFManual },
                ValidationCriteria = new[]
                {
                    "Response demonstrates deep technical understanding",
                    "Response synthesizes information from multiple sources",
                    "Response is comprehensive and educational"
                }
            }
        };
    }

    public async Task<byte[]> GenerateTestCSVAsync(string fileName)
    {
        var csvContent = """
            Make,Model,Year,Engine_Type,Engine_Displacement_CC,Max_Power_HP,Max_Torque_NM,Top_Speed_KMH,Price_USD
            Honda,CBR600RR,2023,4-Stroke DOHC,599,118,64.5,260,12999
            Yamaha,YZF-R1,2023,4-Stroke DOHC,998,200,112.4,299,17399
            Kawasaki,Ninja ZX-10R,2023,4-Stroke DOHC,998,203,114.9,300,16999
            BMW,S1000RR,2023,4-Stroke DOHC,999,205,113.0,299,16995
            Ducati,Panigale V4,2023,4-Stroke Desmo,1103,214,124.0,305,22995
            """;

        return System.Text.Encoding.UTF8.GetBytes(csvContent);
    }

    public async Task<byte[]> GenerateTestPDFContentAsync(string fileName)
    {
        // This would normally generate actual PDF bytes using a PDF library
        // For testing purposes, we'll return text content that represents PDF structure
        var pdfContent = """
            MOTORCYCLE MAINTENANCE MANUAL
            
            Table of Contents:
            1. Engine Maintenance
            2. Brake System Service
            3. Suspension Adjustment
            4. Electrical System
            5. Troubleshooting Guide
            
            Chapter 1: Engine Maintenance
            
            Oil Change Procedure:
            1. Warm engine to operating temperature (5-10 minutes)
            2. Position motorcycle on center stand or lift
            3. Remove drain plug with 17mm wrench
            4. Allow oil to drain completely (15-20 minutes)
            5. Remove and replace oil filter
            6. Install drain plug with new gasket (torque: 25 Nm)
            7. Add new oil through filler cap (capacity: 3.7L)
            8. Check oil level with dipstick
            9. Run engine and check for leaks
            
            WARNING: Always allow engine to cool before servicing
            CAUTION: Dispose of used oil and filter properly
            
            Chapter 2: Brake System Service
            
            Brake Fluid Replacement:
            1. Remove brake fluid reservoir cap
            2. Use brake fluid pump to extract old fluid
            3. Fill with DOT 4 brake fluid
            4. Bleed brake system starting from rear caliper
            5. Check brake lever feel and pedal travel
            6. Test brake operation before riding
            
            Brake Pad Inspection:
            - Minimum thickness: 2mm
            - Check for uneven wear patterns
            - Inspect brake disc for scoring or warping
            - Replace pads and discs as needed
            """;

        return System.Text.Encoding.UTF8.GetBytes(pdfContent);
    }

    public async Task SaveTestDataAsync()
    {
        Directory.CreateDirectory(_testDataPath);

        // Save motorcycle specifications
        var specifications = await GetTestMotorcycleSpecificationsAsync();
        var specificationsJson = JsonSerializer.Serialize(specifications, _jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(_testDataPath, "motorcycle-specifications.json"), specificationsJson);

        // Save test queries
        var queries = await GetTestQueriesAsync();
        var queriesJson = JsonSerializer.Serialize(queries, _jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(_testDataPath, "test-queries.json"), queriesJson);

        // Save test scenarios
        var scenarios = await GetTestScenariosAsync();
        var scenariosJson = JsonSerializer.Serialize(scenarios, _jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(_testDataPath, "test-scenarios.json"), scenariosJson);

        // Generate CSV test data
        var csvData = await GenerateTestCSVAsync("test-specifications.csv");
        await File.WriteAllBytesAsync(Path.Combine(_testDataPath, "test-specifications.csv"), csvData);

        // Generate PDF test data
        var pdfData = await GenerateTestPDFContentAsync("test-manual.pdf");
        await File.WriteAllBytesAsync(Path.Combine(_testDataPath, "test-manual.txt"), pdfData);
    }
}

public class TestScenario
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Queries { get; set; } = Array.Empty<string>();
    public TimeSpan ExpectedResponseTime { get; set; }
    public SearchSource[] ExpectedSources { get; set; } = Array.Empty<SearchSource>();
    public string[] ValidationCriteria { get; set; } = Array.Empty<string>();
}
