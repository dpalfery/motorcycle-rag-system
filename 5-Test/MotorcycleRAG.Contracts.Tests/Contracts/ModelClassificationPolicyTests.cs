using System.Reflection;
using System.Reflection.Emit;

namespace MotorcycleRAG.UnitTests.Contracts;

public sealed class ModelClassificationPolicyTests
{
    private const string DomainEntitiesNamespace = "MotorcycleRAG.Domain.Entities";
    private const string ContractsModelsNamespace = "MotorcycleRAG.Contracts.Models";
    private const string ApplicationDtosNamespace = "MotorcycleRAG.Application.DTOs";

    [Fact]
    public void DomainEntity_WithOnlyIdentityAndProperties_IsRejected()
    {
        var result = Classify(CreateFixture(
            "MotorcycleRAG.Domain",
            DomainEntitiesNamespace,
            "PropertyBagEntity",
            FixtureShape.PropertyBag));

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(violation => violation.Contains(
            "invariant or legal state transition",
            StringComparison.Ordinal));
    }

    [Fact]
    public void DomainEntity_WithRecognizedTransitionAndEncapsulatedState_IsAccepted()
    {
        var result = Classify(CreateFixture(
            "MotorcycleRAG.Domain",
            DomainEntitiesNamespace,
            "BehaviorEntity",
            FixtureShape.RecognizedTransition));

        result.IsValid.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public void DomainEntity_WithPublicSetterThatBypassesInvariants_IsRejected()
    {
        var result = Classify(CreateFixture(
            "MotorcycleRAG.Domain",
            DomainEntitiesNamespace,
            "PubliclyMutableEntity",
            FixtureShape.PublicSetterAndTransition));

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(violation => violation.Contains(
            "public setters",
            StringComparison.Ordinal));
    }

    [Fact]
    public void DomainEntity_WithAbstractTransitionContract_IsRejected()
    {
        var result = Classify(CreateFixture(
            "MotorcycleRAG.Domain",
            DomainEntitiesNamespace,
            "AbstractBehaviorEntity",
            FixtureShape.AbstractTransition));

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(violation => violation.Contains(
            "invariant or legal state transition",
            StringComparison.Ordinal));
    }

    [Fact]
    public void DtoOutsideApprovedContractsOrApplicationBoundary_IsRejected()
    {
        var result = Classify(CreateFixture(
            "MotorcycleRAG.Domain",
            DomainEntitiesNamespace,
            "DomainPlacedDto",
            FixtureShape.PropertyBag));

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(violation => violation.Contains(
            "Contracts.Models or Application",
            StringComparison.Ordinal));
    }

    [Fact]
    public void SharedAndApplicationDtos_UseApprovedBoundariesAndDtoSuffix()
    {
        var sharedResult = Classify(CreateFixture(
            "MotorcycleRAG.Contracts.Models",
            $"{ContractsModelsNamespace}.DTOs",
            "SharedDto",
            FixtureShape.PropertyBag));
        var applicationResult = Classify(CreateFixture(
            "MotorcycleRAG.Application",
            ApplicationDtosNamespace,
            "ApplicationDto",
            FixtureShape.PropertyBag));
        var misnamedResult = Classify(CreateFixture(
            "MotorcycleRAG.Contracts.Models",
            $"{ContractsModelsNamespace}.DTOs",
            "SharedModel",
            FixtureShape.PropertyBag));

        sharedResult.IsValid.Should().BeTrue();
        applicationResult.IsValid.Should().BeTrue();
        misnamedResult.IsValid.Should().BeFalse();
        misnamedResult.Violations.Should().Contain(violation => violation.Contains(
            "Dto suffix",
            StringComparison.Ordinal));
    }

    [Fact]
    public void NamespaceBoundary_AcceptsNestedNamespaces_ButRejectsLookalikes()
    {
        var nestedEntity = CreateFixture(
            "MotorcycleRAG.Domain",
            $"{DomainEntitiesNamespace}.Nested",
            "NestedPropertyBag",
            FixtureShape.PropertyBag);
        var lookalikeEntity = CreateFixture(
            "MotorcycleRAG.Domain",
            "MotorcycleRAG.Domain.EntitiesLike",
            "LookalikeModel",
            FixtureShape.PropertyBag);

        Classify(nestedEntity).IsValid.Should().BeFalse();
        IsInNamespace(nestedEntity.Namespace, DomainEntitiesNamespace).Should().BeTrue();
        IsInNamespace(lookalikeEntity.Namespace, DomainEntitiesNamespace).Should().BeFalse();
    }

    [Fact]
    public void Exception_IsAllowedOnlyWithRegisteredAdrAndRationale()
    {
        var type = CreateFixture(
            "MotorcycleRAG.Domain",
            DomainEntitiesNamespace,
            "ApprovedLegacyProjection",
            FixtureShape.PropertyBag);

        Classify(type).IsValid.Should().BeFalse();

        var malformedApproval = Classify(
            type,
            new Dictionary<string, ArchitectureApproval>(StringComparer.Ordinal)
            {
                [type.FullName!] = new("not-an-adr", "has a rationale")
            });
        malformedApproval.IsValid.Should().BeFalse();
        malformedApproval.Violations.Should().Contain(violation => violation.Contains(
            "registered ADR",
            StringComparison.Ordinal));

        var approved = Classify(
            type,
            new Dictionary<string, ArchitectureApproval>(StringComparer.Ordinal)
            {
                [type.FullName!] = new("ADR-2026-07-13", "legacy persistence projection pending migration")
            });

        approved.IsValid.Should().BeTrue();
        approved.ApprovalReason.Should().Contain("ADR-2026-07-13");
    }

    /// <summary>
    /// Post-migration gate. It is opt-in while packages 2-5 are migrating the existing property
    /// bags. Remove the skip after migration; the inventory then fails on every new property-bag
    /// or publicly mutable type under Domain.Entities and has no per-type exemptions.
    /// </summary>
    [Fact(Skip = "Enable after domain entity/DTO migration packages 2-5 complete by removing this skip.")]
    public void DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes()
    {
        var domainAssembly = typeof(MotorcycleRAG.Domain.Entities.BikeModel).Assembly;
        var violations = domainAssembly
            .GetTypes()
            .Where(type => IsInNamespace(type.Namespace, DomainEntitiesNamespace))
            .SelectMany(type => FindViolations(new ModelDeclaration(type)))
            .ToArray();

        violations.Should().BeEmpty();
    }

    private static ClassificationResult Classify(
        Type type,
        IReadOnlyDictionary<string, ArchitectureApproval>? approvals = null) =>
        Classify(new ModelDeclaration(type), approvals);

    private static ClassificationResult Classify(
        ModelDeclaration declaration,
        IReadOnlyDictionary<string, ArchitectureApproval>? approvals = null)
    {
        var approvalViolations = ValidateApproval(declaration, approvals, out var approvalReason);
        if (approvalViolations.Count > 0)
        {
            return new ClassificationResult(false, approvalViolations, approvalReason);
        }

        if (approvalReason is not null)
        {
            return new ClassificationResult(true, Array.Empty<string>(), approvalReason);
        }

        var violations = FindViolations(declaration);
        return new ClassificationResult(violations.Count == 0, violations, null);
    }

    private static IReadOnlyList<string> FindViolations(ModelDeclaration declaration)
    {
        var violations = new List<string>();
        var isDomainEntity = IsInNamespace(declaration.Namespace, DomainEntitiesNamespace);
        var isDto = declaration.Type.Name.EndsWith("Dto", StringComparison.Ordinal);

        if (isDomainEntity && !HasRecognizedDomainBehavior(declaration.Type))
        {
            violations.Add("Domain Entities must declare an invariant or legal state transition");
        }

        if (isDomainEntity && HasPublicPropertySetter(declaration.Type))
        {
            violations.Add("Domain Entity state must not use public setters");
        }

        if (IsDtoBoundaryCandidate(declaration) && !isDto)
        {
            violations.Add("DTOs in approved boundaries must use the Dto suffix");
        }

        if (isDto && !IsApprovedDtoBoundary(declaration))
        {
            violations.Add("DTOs must live in Contracts.Models or Application DTO boundaries");
        }

        return violations;
    }

    private static List<string> ValidateApproval(
        ModelDeclaration declaration,
        IReadOnlyDictionary<string, ArchitectureApproval>? approvals,
        out string? approvalReason)
    {
        approvalReason = null;
        if (approvals is null || !approvals.TryGetValue(declaration.Type.FullName!, out var approval))
        {
            return [];
        }

        var violations = new List<string>();
        var registry = LoadAdrRegistry();
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                approval.AdrKey,
                "^ADR-[0-9]{4}-[0-9]{2}-[0-9]{2}$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || !registry.TryGetValue(approval.AdrKey, out var registeredAdr))
        {
            violations.Add("Exceptions require a registered ADR key");
        }

        if (string.IsNullOrWhiteSpace(approval.Rationale))
        {
            violations.Add("Exceptions require a non-empty ADR rationale");
        }

        if (registry.TryGetValue(approval.AdrKey, out registeredAdr)
            && (registeredAdr.Status != "Accepted" || string.IsNullOrWhiteSpace(registeredAdr.Rationale)))
        {
            violations.Add("Registered ADRs require Accepted status and a non-empty rationale");
        }

        if (violations.Count == 0)
        {
            approvalReason = approval.AdrKey;
        }

        return violations;
    }

    private static IReadOnlyDictionary<string, AdrRecord> LoadAdrRegistry()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "6-Docs", "adr");
        if (!Directory.Exists(directory))
        {
            return new Dictionary<string, AdrRecord>(StringComparer.Ordinal);
        }

        return Directory.EnumerateFiles(directory, "ADR-*.md", SearchOption.TopDirectoryOnly)
            .Select(ParseAdr)
            .Where(record => record is not null)
            .Cast<AdrRecord>()
            .ToDictionary(record => record.Key, StringComparer.Ordinal);
    }

    private static AdrRecord? ParseAdr(string path)
    {
        var content = File.ReadAllText(path);
        var keyMatch = System.Text.RegularExpressions.Regex.Match(
            content,
            "(?m)^#\\s+(ADR-[0-9]{4}-[0-9]{2}-[0-9]{2})(?:\\s|:)");
        var statusMatch = System.Text.RegularExpressions.Regex.Match(
            content,
            "(?m)^\\*\\*Status:\\*\\*\\s*(?<status>.+?)\\s*$");
        var rationaleMatch = System.Text.RegularExpressions.Regex.Match(
            content,
            "(?ms)^##\\s+Rationale\\s*(?<rationale>.*?)(?=^##\\s|\\z)");

        if (!keyMatch.Success || !statusMatch.Success || !rationaleMatch.Success)
        {
            return null;
        }

        return new AdrRecord(
            keyMatch.Groups[1].Value,
            statusMatch.Groups["status"].Value.Trim(),
            rationaleMatch.Groups["rationale"].Value.Trim());
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(current.FullName, "6-Docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with AGENTS.md and 6-Docs was not found.");
    }

    private static bool HasRecognizedDomainBehavior(Type type) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(method => method.DeclaringType == type
                && !method.IsStatic
                && !method.IsAbstract
                && method.GetMethodBody() is not null
                && !method.IsSpecialName
                && IsRecognizedTransitionName(method.Name));

    private static bool IsRecognizedTransitionName(string methodName) =>
        methodName is "Activate" or "Archive" or "Cancel" or "Complete" or "Disable" or "Enable" or "Fail" or "Start" or "Validate"
        || methodName.StartsWith("Mark", StringComparison.Ordinal)
        || methodName.StartsWith("Transition", StringComparison.Ordinal)
        || methodName.StartsWith("Update", StringComparison.Ordinal);

    private static bool HasPublicPropertySetter(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(property => property.SetMethod?.IsPublic == true);

    private static bool IsDtoBoundaryCandidate(ModelDeclaration declaration) =>
        (declaration.AssemblyName == "MotorcycleRAG.Contracts.Models"
            && IsInNamespace(declaration.Namespace, ContractsModelsNamespace))
        || (declaration.AssemblyName == "MotorcycleRAG.Application"
            && IsInNamespace(declaration.Namespace, ApplicationDtosNamespace));

    private static bool IsApprovedDtoBoundary(ModelDeclaration declaration) =>
        declaration.Type.Name.EndsWith("Dto", StringComparison.Ordinal)
        && ((declaration.AssemblyName == "MotorcycleRAG.Contracts.Models"
                && IsInNamespace(declaration.Namespace, ContractsModelsNamespace))
            || (declaration.AssemblyName == "MotorcycleRAG.Application"
                && IsInNamespace(declaration.Namespace, ApplicationDtosNamespace)));

    private static bool IsInNamespace(string? actualNamespace, string requiredPrefix) =>
        actualNamespace == requiredPrefix
        || (actualNamespace?.StartsWith($"{requiredPrefix}.", StringComparison.Ordinal) ?? false);

    private static Type CreateFixture(
        string assemblyName,
        string namespaceName,
        string typeName,
        FixtureShape shape)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assemblyName),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("ModelClassificationFixtures");
        var builder = module.DefineType(
            $"{namespaceName}.{typeName}",
            (shape == FixtureShape.AbstractTransition ? TypeAttributes.Abstract : TypeAttributes.NotPublic)
            | TypeAttributes.Class);

        if (shape != FixtureShape.AbstractTransition)
        {
            DefineProperty(builder, "Id", typeof(Guid), shape is FixtureShape.PropertyBag or FixtureShape.PublicSetterAndTransition);
        }

        if (shape is FixtureShape.RecognizedTransition or FixtureShape.PublicSetterAndTransition or FixtureShape.AbstractTransition)
        {
            var attributes = MethodAttributes.Public;
            if (shape == FixtureShape.AbstractTransition)
            {
                attributes |= MethodAttributes.Virtual | MethodAttributes.Abstract;
            }

            var method = builder.DefineMethod(
                "Activate",
                attributes,
                typeof(void),
                Type.EmptyTypes);

            if (shape != FixtureShape.AbstractTransition)
            {
                method.GetILGenerator().Emit(OpCodes.Ret);
            }
        }

        return builder.CreateType()!;
    }

    private static void DefineProperty(TypeBuilder builder, string name, Type propertyType, bool publicSetter)
    {
        var field = builder.DefineField($"_{name}", propertyType, FieldAttributes.Private);
        var property = builder.DefineProperty(name, PropertyAttributes.None, propertyType, Type.EmptyTypes);
        var getter = builder.DefineMethod(
            $"get_{name}",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            propertyType,
            Type.EmptyTypes);
        var getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, field);
        getterIl.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);

        if (publicSetter)
        {
            var setter = builder.DefineMethod(
                $"set_{name}",
                MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(void),
                [propertyType]);
            var setterIl = setter.GetILGenerator();
            setterIl.Emit(OpCodes.Ldarg_0);
            setterIl.Emit(OpCodes.Ldarg_1);
            setterIl.Emit(OpCodes.Stfld, field);
            setterIl.Emit(OpCodes.Ret);
            property.SetSetMethod(setter);
        }
    }

    private sealed record ModelDeclaration(Type Type)
    {
        public string AssemblyName => Type.Assembly.GetName().Name!;
        public string? Namespace => Type.Namespace;
    }

    private sealed record ArchitectureApproval(string AdrKey, string Rationale);

    private sealed record AdrRecord(string Key, string Status, string Rationale);

    private sealed record ClassificationResult(
        bool IsValid,
        IReadOnlyList<string> Violations,
        string? ApprovalReason);

    private enum FixtureShape
    {
        PropertyBag,
        RecognizedTransition,
        PublicSetterAndTransition,
        AbstractTransition
    }

}
