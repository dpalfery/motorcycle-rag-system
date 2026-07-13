using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Domian.Tests.Domain;

public sealed class MotorcycleContentModelTests
{
    [Fact]
    public void MotorcycleDocument_WhenCreated_ProvidesIndependentMetadataAndHeadingCollections()
    {
        // Arrange
        var first = new MotorcycleDocument();
        var second = new MotorcycleDocument();

        // Act
        first.Metadata.Tags.Add("maintenance");
        first.SectionHeadings.Add("Engine");

        // Assert
        first.Id.Should().BeEmpty();
        first.Title.Should().BeEmpty();
        first.Content.Should().BeEmpty();
        first.Type.Should().Be(DocumentType.Specification);
        first.Metadata.Tags.Should().ContainSingle().Which.Should().Be("maintenance");
        first.SectionHeadings.Should().ContainSingle().Which.Should().Be("Engine");
        second.Metadata.Tags.Should().BeEmpty();
        second.SectionHeadings.Should().BeEmpty();
    }

    [Fact]
    public void MotorcycleDocument_WhenCitationLocatorsAreAssigned_RetainsSearchableContentState()
    {
        // Arrange
        var createdAt = DateTime.Parse("2026-07-12T12:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var contentVector = new[] { 0.1f, 0.2f };
        var metadata = new DocumentMetadata { PageNumber = 18, Section = "Lubrication" };

        // Act
        var document = new MotorcycleDocument
        {
            Id = "manual-18-chunk-2",
            Title = "Lubricating the chain",
            Content = "Apply lubricant to the inside run of the chain.",
            Type = DocumentType.MaintenanceGuide,
            Metadata = metadata,
            ContentVector = contentVector,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            PageNumber = 18,
            PageRange = "18-19",
            PrimarySection = "Maintenance",
            SectionLevel = 2,
            TableCaption = "Lubrication intervals",
            ChunkIndex = 2
        };

        // Assert
        document.Should().BeEquivalentTo(new
        {
            Id = "manual-18-chunk-2",
            Title = "Lubricating the chain",
            Content = "Apply lubricant to the inside run of the chain.",
            Type = DocumentType.MaintenanceGuide,
            Metadata = metadata,
            ContentVector = contentVector,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            PageNumber = 18,
            PageRange = "18-19",
            PrimarySection = "Maintenance",
            SectionLevel = 2,
            TableCaption = "Lubrication intervals",
            ChunkIndex = 2
        });
    }

    [Fact]
    public void MotorcycleManual_WhenConfigured_RetainsSourceIngestionTraceability()
    {
        // Arrange
        var sourceJobId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-07-12T12:00:00+00:00");

        // Act
        var manual = new MotorcycleManual
        {
            Make = "Honda",
            Model = "CBR600RR",
            Year = 2024,
            OriginalFileName = "cbr600rr-service-manual.pdf",
            TotalPages = 350,
            CreatedAtUtc = createdAt,
            SourceIngestionJobId = sourceJobId
        };

        // Assert
        manual.ManualDocumentId.Should().NotBe(Guid.Empty);
        manual.Make.Should().Be("Honda");
        manual.Model.Should().Be("CBR600RR");
        manual.Year.Should().Be(2024);
        manual.OriginalFileName.Should().Be("cbr600rr-service-manual.pdf");
        manual.TotalPages.Should().Be(350);
        manual.CreatedAtUtc.Should().Be(createdAt);
        manual.SourceIngestionJobId.Should().Be(sourceJobId);
    }

    [Fact]
    public void MotorcycleManual_WhenMetadataIsUpdated_RetainsUpdateTimestamp()
    {
        // Arrange
        var updatedAt = DateTimeOffset.Parse("2026-07-12T13:00:00+00:00");

        // Act
        var manual = new MotorcycleManual { UpdatedAtUtc = updatedAt };

        // Assert
        manual.UpdatedAtUtc.Should().Be(updatedAt);
    }

    [Fact]
    public void MotorcycleSpecification_WhenConfigured_RetainsSpecificationsAndIndependentFeatureCollections()
    {
        // Arrange
        var priceDate = DateTime.Parse("2026-07-12T12:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var first = new MotorcycleSpecification
        {
            Id = "cbr600rr-2024",
            Make = "Honda",
            Model = "CBR600RR",
            Year = 2024,
            Engine = new EngineSpecification
            {
                Type = "Inline-four",
                DisplacementCC = 599,
                Horsepower = 119,
                Torque = 63,
                FuelSystem = "PGM-DSFI",
                Cylinders = 4
            },
            Performance = new PerformanceMetrics
            {
                TopSpeedKmh = 265,
                Acceleration0To100 = 3.5m,
                FuelConsumptionL100Km = 5.8m,
                RangeKm = 280
            },
            Safety = new SafetyFeatures { Abs = true, TractionControl = true },
            Pricing = new PricingInformation { Msrp = 12499m, Currency = "USD", Market = "US", PriceDate = priceDate }
        };
        var second = new MotorcycleSpecification { Safety = new SafetyFeatures() };

        // Act
        first.AdditionalSpecs.Add("Frame", "Aluminum twin-spar");
        first.Safety!.AdditionalFeatures.Add("Honda Selectable Torque Control");

        // Assert
        first.Id.Should().Be("cbr600rr-2024");
        first.Engine.Should().BeEquivalentTo(new EngineSpecification
        {
            Type = "Inline-four",
            DisplacementCC = 599,
            Horsepower = 119,
            Torque = 63,
            FuelSystem = "PGM-DSFI",
            Cylinders = 4
        });
        first.Performance.Should().BeEquivalentTo(new PerformanceMetrics
        {
            TopSpeedKmh = 265,
            Acceleration0To100 = 3.5m,
            FuelConsumptionL100Km = 5.8m,
            RangeKm = 280
        });
        first.Pricing.Should().BeEquivalentTo(new PricingInformation { Msrp = 12499m, Currency = "USD", Market = "US", PriceDate = priceDate });
        first.AdditionalSpecs.Should().ContainKey("Frame").WhoseValue.Should().Be("Aluminum twin-spar");
        first.Safety.AdditionalFeatures.Should().ContainSingle().Which.Should().Be("Honda Selectable Torque Control");
        second.AdditionalSpecs.Should().BeEmpty();
        second.Safety.AdditionalFeatures.Should().BeEmpty();
    }

    [Fact]
    public void SafetyFeatures_WhenActiveSafetySystemsAreConfigured_RetainsTheirState()
    {
        // Arrange
        var safety = new SafetyFeatures();

        // Act
        safety.StabilityControl = true;
        safety.AntiWheelieControl = true;

        // Assert
        safety.StabilityControl.Should().BeTrue();
        safety.AntiWheelieControl.Should().BeTrue();
        safety.Abs.Should().BeFalse();
        safety.TractionControl.Should().BeFalse();
    }

    [Fact]
    public void PricingInformation_WhenCreated_UsesUsdAndCurrentPriceDate()
    {
        // Arrange
        var pricing = new PricingInformation();

        // Act
        var state = new { pricing.Currency, pricing.Market, pricing.PriceDate, pricing.Msrp };

        // Assert
        state.Currency.Should().Be("USD");
        state.Market.Should().BeEmpty();
        state.PriceDate.Should().BeAfter(DateTime.UnixEpoch);
        state.Msrp.Should().Be(0m);
    }
}
