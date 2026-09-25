using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

public sealed class TypeTestPlannerTests
{
    [Fact]
    public void FactoryCreatesPlannerWithNullableSupport()
    {
        var target = CreateReferenceType("Marker");
        var planner = new TypeTestPlannerFactory(new NullableTypeResolver()).Create(
            new RecordingTypeOperandResolver(target),
            new FixedRelationshipClassifier(isAssignmentCompatible: true));

        Assert.IsType<TypeTestPlanner>(planner);
    }

    [Fact]
    public void BuildPlansReferenceSpecializedUnboxAnySites()
    {
        var target = CreateReferenceType("Marker");
        var allocated = CreateReferenceType("MarkerImplementation");
        var laterAllocated = CreateReferenceType("ZMarkerImplementation");
        var method = CreateMethodInstance(target);
        var instruction = CreateTypeInstruction(CilOperation.UnboxAny);
        var typeOperands = new RecordingTypeOperandResolver(target);
        var planner = CreatePlanner(
            typeOperands,
            new FixedRelationshipClassifier(isAssignmentCompatible: true));

        var sites = planner.Build(
            [CreateManagedMethodBody([instruction], instance: method)],
            [laterAllocated, allocated]);

        var site = Assert.Single(sites).Value;
        Assert.Equal(target, site.TargetType);
        Assert.Collection(
            site.MatchingTypes,
            first => Assert.Same(allocated, first),
            second => Assert.Same(laterAllocated, second));
        Assert.Same(method, typeOperands.LastMethod);
        Assert.Equal(1, typeOperands.CallCount);
    }

    [Fact]
    public void PlanIncludesGenericVarianceCompatibleRuntimeTypes()
    {
        var target = CreateReferenceType("Marker");
        var allocated = CreateReferenceType("MarkerImplementation");
        var laterAllocated = CreateReferenceType("ZMarkerImplementation");
        var method = CreateMethodInstance(target);
        var instruction = CreateTypeInstruction(CilOperation.UnboxAny);
        var typeOperands = new RecordingTypeOperandResolver(target);
        var planner = CreatePlanner(
            typeOperands,
            new VarianceRelationshipClassifier());

        var sites = planner.Build(
            [CreateManagedMethodBody([instruction], instance: method)],
            [laterAllocated, allocated]);

        var site = Assert.Single(sites).Value;
        Assert.Equal(target, site.TargetType);
        Assert.Collection(
            site.MatchingTypes,
            first => Assert.Same(allocated, first),
            second => Assert.Same(laterAllocated, second));
        Assert.Same(method, typeOperands.LastMethod);
        Assert.Equal(1, typeOperands.CallCount);
    }

    [Fact]
    public void BuildOmitsValueSpecializedUnboxAnySites()
    {
        var target = CliTypeIdentity.Primitive("Int32", CliValueKind.I4);
        var method = CreateMethodInstance(target);
        var typeOperands = new RecordingTypeOperandResolver(target);
        var relationships = new FixedRelationshipClassifier(isAssignmentCompatible: true);
        var planner = CreatePlanner(typeOperands, relationships);

        var sites = planner.Build(
            [CreateManagedMethodBody(
                [CreateTypeInstruction(CilOperation.UnboxAny)],
                instance: method)],
            [target]);

        Assert.Empty(sites);
        Assert.Equal(1, typeOperands.CallCount);
        Assert.Equal(0, relationships.CallCount);
    }

    [Fact]
    public void BuildRetainsCastClassAndIsInstanceSites()
    {
        var target = CreateReferenceType("Marker");
        var method = CreateMethodInstance(target);
        var typeOperands = new RecordingTypeOperandResolver(target);
        var planner = CreatePlanner(
            typeOperands,
            new FixedRelationshipClassifier(isAssignmentCompatible: false));
        var instructions = new[]
        {
            CreateTypeInstruction(CilOperation.CastClass, offset: 0),
            CreateTypeInstruction(CilOperation.IsInstance, offset: 1),
        };

        var sites = planner.Build(
            [CreateManagedMethodBody(instructions, instance: method)],
            []);

        Assert.Equal(2, sites.Count);
        Assert.All(sites.Values, site => Assert.Empty(site.MatchingTypes));
        Assert.Equal(2, typeOperands.CallCount);
    }

    [Fact]
    public void BuildIgnoresInstructionsOutsideTheTypeTestFamily()
    {
        var target = CreateReferenceType("Marker");
        var typeOperands = new RecordingTypeOperandResolver(target);
        var planner = CreatePlanner(
            typeOperands,
            new FixedRelationshipClassifier(isAssignmentCompatible: true));

        var sites = planner.Build(
            [CreateManagedMethodBody(
                [new CilInstruction(0, 1, CilOperation.DefaultValue, new CilOperand.TypeIdentity(target))])],
            [target]);

        Assert.Empty(sites);
        Assert.Equal(0, typeOperands.CallCount);
    }

    [Fact]
    public void BuildMatchesNullableMembershipAgainstTheBoxedUnderlyingType()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                new AssemblyIdentity("System.Private.CoreLib"),
                "System",
                "Nullable`1",
                true),
            [underlying]);
        var method = CreateMethodInstance(nullable);
        var typeOperands = new RecordingTypeOperandResolver(nullable);
        var relationships = new RecordingRelationshipClassifier(underlying);
        var planner = CreatePlanner(typeOperands, relationships);

        var sites = planner.Build(
            [CreateManagedMethodBody(
                [CreateTypeInstruction(CilOperation.IsInstance)],
                instance: method)],
            [underlying]);

        var site = Assert.Single(sites).Value;
        Assert.Equal(nullable, site.TargetType);
        Assert.Equal(underlying, Assert.Single(site.MatchingTypes));
        Assert.Equal(underlying, relationships.LastTarget);
    }

    private static ManagedMethodBody CreateManagedMethodBody(
        IEnumerable<CilInstruction> instructions,
        MethodInstanceModel? instance = null)
    {
        var body = instructions.ToArray();
        var returnOffset = body.Max(static instruction => instruction.Offset) + 1;

        return ValidationTestData.ManagedMethodBody(
            [.. body, new CilInstruction(
                returnOffset,
                returnOffset + 1,
                CilOperation.Return,
                new CilOperand.None())],
            instance: instance);
    }

    private static CilInstruction CreateTypeInstruction(CilOperation operation, int offset = 0) =>
        new(
            offset,
            offset + 1,
            operation,
            new CilOperand.TypeIdentity(CliTypeIdentity.GenericParameter(method: true, index: 0)));

    private static MethodInstanceModel CreateMethodInstance(CliTypeIdentity methodArgument)
    {
        var assembly = new AssemblyIdentity("TypeTestPlannerTests");
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            new EntityKey(assembly, 2),
            "Cast",
            true,
            new MethodSignatureModel(CliValueKind.ManagedReference, []),
            0);
        return new(
            definition,
            CreateReferenceType("DeclaringType"),
            [methodArgument],
            definition.Signature);
    }

    private static CliTypeIdentity CreateReferenceType(string name) =>
        CliTypeIdentity.Named(
            new AssemblyIdentity("TypeTestPlannerTests"),
            "TypeTestPlannerTests",
            name,
            isValueType: false,
            CliValueKind.ManagedReference);

    private static ITypeTestPlanner ThroughContract(ITypeTestPlanner planner) => planner;

    private static ITypeTestPlanner CreatePlanner(
        ITypeOperandResolver typeOperands,
        ITypeRelationshipClassifier relationships) =>
        ThroughContract(new TypeTestPlanner(
            typeOperands,
            relationships,
            new NullableTypeResolver()));

    private sealed class RecordingTypeOperandResolver(CliTypeIdentity result) : ITypeOperandResolver
    {
        public int CallCount { get; private set; }

        public MethodInstanceModel? LastMethod { get; private set; }

        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance)
        {
            CallCount++;
            LastMethod = methodInstance;
            return result;
        }
    }

    private sealed class FixedRelationshipClassifier(bool isAssignmentCompatible) :
        ITypeRelationshipClassifier
    {
        public int CallCount { get; private set; }

        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            CallCount++;
            return new((isAssignmentCompatible
                    ? TypeRelationshipCharacteristics.Hierarchy
                    : TypeRelationshipCharacteristics.None));
        }
    }

    private sealed class RecordingRelationshipClassifier(
        CliTypeIdentity compatibleType) : ITypeRelationshipClassifier
    {
        public CliTypeIdentity? LastTarget { get; private set; }

        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target)
        {
            LastTarget = target;
            return new(candidate == compatibleType && target == compatibleType
                ? TypeRelationshipCharacteristics.Hierarchy
                : TypeRelationshipCharacteristics.None);
        }
    }

    private sealed class VarianceRelationshipClassifier :
        ITypeRelationshipClassifier
    {
        public int CallCount { get; private set; }

        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            CallCount++;
            return new(TypeRelationshipCharacteristics.GenericVariance);
        }
    }
}
