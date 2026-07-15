using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.API.Configuration.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.DataProcessing;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public sealed class DataProcessorsConfigurationCoverageTests
{
    [Fact]
    public void AddDataProcessors_WithValidDocumentIntelligenceEndpoint_RegistersPdfProcessor()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAI:DocumentIntelligenceEndpoint"] = "https://document-intelligence.example.test"
        }).Build();
        var services = new ServiceCollection();

        services.AddDataProcessors(configuration);

        services.Single(descriptor => descriptor.ServiceType == typeof(IDataProcessor<CSVFile>)).ImplementationType
            .Should().Be<MotorcycleCsvProcessor>();
        services.Single(descriptor => descriptor.ServiceType == typeof(IDataProcessor<PDFDocument>)).ImplementationType
            .Should().Be<MotorcyclePdfProcessor>();
    }
}
