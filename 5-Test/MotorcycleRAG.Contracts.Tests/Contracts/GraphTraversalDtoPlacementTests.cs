using System.Reflection;
using System.Reflection.Emit;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;

namespace MotorcycleRAG.UnitTests.Contracts;

public sealed class GraphTraversalDtoPlacementTests
{
    [Fact]
    public void ContractsAssembly_HasNoConcreteOrStaticProductionTypes()
    {
        var contractsAssembly = typeof(IGraphRepository).Assembly;
        var contractsAssemblyName = contractsAssembly.GetName().Name!;
        var nonInterfaceTypes = contractsAssembly
            .GetTypes()
            .Where(type => IsForbiddenProductionType(type, contractsAssemblyName))
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToArray();

        nonInterfaceTypes.Should().BeEmpty("MotorcycleRAG.Contracts is reserved for interfaces");
    }

    [Fact]
    public void ContractsPlacementGuard_WhenCoverletInjectsModuleTracker_AllowsOnlyTheExactTrackerType()
    {
        var contractsAssemblyName = typeof(IGraphRepository).Assembly.GetName().Name!;

        IsCoverletModuleTracker(
                "Coverlet.Core.Instrumentation.Tracker",
                $"{contractsAssemblyName}_{Guid.NewGuid():D}",
                contractsAssemblyName)
            .Should().BeTrue();
    }

    [Fact]
    public void ContractsPlacementGuard_WhenInternalConcreteContractsTypeExists_DetectsIt()
    {
        var contractsAssemblyName = typeof(IGraphRepository).Assembly.GetName().Name!;
        var internalContractsType = CreateInternalConcreteType(
            "MotorcycleRAG.Contracts.Internal",
            "InternalConcreteType");

        IsForbiddenProductionType(internalContractsType, contractsAssemblyName).Should().BeTrue();
    }

    [Fact]
    public void ContractsPlacementGuard_WhenCoverletNamespaceDoesNotHaveModuleTrackerName_DetectsIt()
    {
        var contractsAssemblyName = typeof(IGraphRepository).Assembly.GetName().Name!;
        var unexpectedCoverletType = CreateInternalConcreteType(
            "Coverlet.Core.Instrumentation.Tracker",
            "UnexpectedConcreteType");

        IsForbiddenProductionType(unexpectedCoverletType, contractsAssemblyName).Should().BeTrue();
    }

    [Fact]
    public void GraphTraversalDtos_AreOwnedByContractsModels()
    {
        var contractsAssembly = typeof(IGraphRepository).Assembly;
        var contractsModelsAssembly = typeof(IngestionJobConfiguration).Assembly;

        ReferenceEquals(typeof(GraphPathResultDto).Assembly, contractsModelsAssembly).Should().BeTrue();
        ReferenceEquals(typeof(GraphTraversalResultDto).Assembly, contractsModelsAssembly).Should().BeTrue();
        contractsAssembly.GetType(typeof(GraphPathResultDto).FullName!).Should().BeNull();
        contractsAssembly.GetType(typeof(GraphTraversalResultDto).FullName!).Should().BeNull();
    }

    private static bool IsForbiddenProductionType(Type type, string contractsAssemblyName) =>
        type.IsClass
        && !typeof(MulticastDelegate).IsAssignableFrom(type)
        && !IsCoverletModuleTracker(type.Namespace, type.Name, contractsAssemblyName);

    private static bool IsCoverletModuleTracker(
        string? typeNamespace,
        string typeName,
        string contractsAssemblyName)
    {
        const string coverletTrackerNamespace = "Coverlet.Core.Instrumentation.Tracker";
        var namePrefix = $"{contractsAssemblyName}_";
        return typeNamespace == coverletTrackerNamespace
            && typeName.StartsWith(namePrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(typeName[namePrefix.Length..], "D", out _);
    }

    private static Type CreateInternalConcreteType(string @namespace, string name)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"ContractsPlacementGuard_{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");
        return module.DefineType(
                $"{@namespace}.{name}",
                TypeAttributes.NotPublic | TypeAttributes.Class)
            .CreateType()!;
    }
}
