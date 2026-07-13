using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.Persistence.Tests.Sql;

public class SqlServiceCollectionExtensionsTests
{
    private static IConfiguration CreateConfiguration(string? connectionString = null)
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Sql:ConnectionString"] = connectionString ?? "Server=localhost;Database=test;Trusted_Connection=True;",
            ["Sql:CommandTimeout"] = "30",
            ["Sql:ConnectionTimeout"] = "15",
            ["Sql:MaxPoolSize"] = "50"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public void AddSqlPersistenceServices_RegistersSqlOptions()
    {
        var services = new ServiceCollection();
        var config = CreateConfiguration();

        services.AddSqlPersistenceServices(config);

        // Verify SqlOptions can be resolved
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<Microsoft.Extensions.Options.IOptions<SqlOptions>>();
        options.Should().NotBeNull();
        options!.Value.ConnectionString.Should().Be("Server=localhost;Database=test;Trusted_Connection=True;");
    }

    [Fact]
    public void AddSqlPersistenceServices_RegistersSqlConnectionFactoryAsSingleton()
    {
        var services = new ServiceCollection();
        var config = CreateConfiguration();

        services.AddSqlPersistenceServices(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISqlConnectionFactory));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddSqlPersistenceServices_RegistersRepositoriesAsScoped()
    {
        var services = new ServiceCollection();
        var config = CreateConfiguration();

        services.AddSqlPersistenceServices(config);

        // All repositories should be registered as scoped
        var repoRegistrations = services
            .Where(d => d.Lifetime == ServiceLifetime.Scoped)
            .ToList();

        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IIngestionJobRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IGraphRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IBikeModelRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IBikeModelCategoryRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IAuditRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IUserRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IUsageRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IPlanRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IWebSourceRepository));
        repoRegistrations.Should().Contain(d => d.ServiceType == typeof(IManualDocumentRepository));
    }

    [Fact]
    public void AddSqlPersistenceServices_RegistersWebTrustPolicyStoreAsSingleton()
    {
        var services = new ServiceCollection();
        var config = CreateConfiguration();

        services.AddSqlPersistenceServices(config);

        var webTrustStore = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IWebTrustPolicyStore) &&
            d.Lifetime == ServiceLifetime.Singleton);
        webTrustStore.Should().NotBeNull();
    }

    [Fact]
    public void AddSqlPersistenceServices_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        var config = CreateConfiguration();

        var result = services.AddSqlPersistenceServices(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddSqlPersistenceServices_WithNullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSqlPersistenceServices(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSqlPersistenceServices_RegistersSqlOptionsValidator()
    {
        var services = new ServiceCollection();
        var config = CreateConfiguration();

        services.AddSqlPersistenceServices(config);

        var provider = services.BuildServiceProvider();
        var validator = provider.GetService<
            Microsoft.Extensions.Options.IValidateOptions<SqlOptions>>();
        validator.Should().NotBeNull();
    }

    [Fact]
    public void SqlOptionsValidator_WithValidOptions_ReturnsSuccess()
    {
        var validator = new SqlOptionsValidator();
        var options = new SqlOptions
        {
            ConnectionString = "Server=localhost;Database=test;",
            CommandTimeout = 30,
            ConnectionTimeout = 15,
            MaxPoolSize = 100
        };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void SqlOptionsValidator_WithMissingConnectionString_ReturnsFailure()
    {
        var validator = new SqlOptionsValidator();
        var options = new SqlOptions
        {
            ConnectionString = "",
            CommandTimeout = 30,
            ConnectionTimeout = 15,
            MaxPoolSize = 100
        };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ConnectionString");
    }

    [Fact]
    public void SqlOptionsValidator_WithInvalidCommandTimeout_ReturnsFailure()
    {
        var validator = new SqlOptionsValidator();
        var options = new SqlOptions
        {
            ConnectionString = "Server=localhost;",
            CommandTimeout = 0,
            ConnectionTimeout = 15,
            MaxPoolSize = 100
        };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("CommandTimeout");
    }

    [Fact]
    public void SqlOptionsValidator_WithNullOptions_ThrowsArgumentNullException()
    {
        var validator = new SqlOptionsValidator();

        var act = () => validator.Validate(null, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
