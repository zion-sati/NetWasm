using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Tests.Analysis.Delegates;

public sealed class DelegateBindingPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("delegate-tests");
    private static readonly EntityKey TypeKey = new(Assembly, 1);

    [Fact]
    public void PlannerRecordsIdentityAndReferenceConversions()
    {
        var baseType = Type("Base");
        var derivedType = Type("Derived");
        var relationships = new RecordingRelationships(
            (derivedType, baseType));
        var planner = new[]
        {
            new DelegateBindingPlanner(
                relationships,
                new ManagedMethodIdentityFactory()),
        }.Cast<IDelegateBindingPlanner>().Single();
        var invoke = Method(
            "Invoke",
            Type("Callback"),
            baseType,
            derivedType,
            baseType);
        var target = Method(
            "Target",
            Type("TargetOwner"),
            derivedType,
            baseType,
            baseType);

        var binding = Assert.Single(planner.Plan([invoke], [target]));

        Assert.Equal(invoke.CanonicalName, binding.InvokeIdentity.CanonicalName);
        Assert.Equal(target.CanonicalName, binding.TargetIdentity.CanonicalName);
        Assert.Same(invoke, binding.Invoke);
        Assert.Same(target, binding.Target);
        Assert.Equal(
            ManagedDelegateAdaptation.ReferenceConversion,
            binding.ParameterBindings[0].Adaptation);
        Assert.Equal(
            ManagedDelegateAdaptation.Identity,
            binding.ParameterBindings[1].Adaptation);
        Assert.Equal(
            ManagedDelegateAdaptation.ReferenceConversion,
            binding.ReturnBinding.Adaptation);
        Assert.Equal(
            [(derivedType, baseType), (derivedType, baseType)],
            relationships.Requests);
    }

    [Fact]
    public void PlannerRejectsIncompatibleParameterAndReturnShapes()
    {
        var reference = Type("Reference");
        var otherReference = Type("OtherReference");
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var wideScalar = CliTypeIdentity.FromStackKind(CliValueKind.I8);
        var planner = new[]
        {
            new DelegateBindingPlanner(
                new RecordingRelationships(),
                new ManagedMethodIdentityFactory()),
        }.Cast<IDelegateBindingPlanner>().Single();
        var invoke = Method("Invoke", Type("Callback"), scalar, reference);
        var wrongCount = Method("WrongCount", Type("Owner1"), scalar);
        var wrongParameter = Method(
            "WrongParameter",
            Type("Owner2"),
            scalar,
            otherReference);
        var wrongReturn = Method(
            "WrongReturn",
            Type("Owner3"),
            wideScalar,
            reference);

        var bindings = planner.Plan(
            [invoke],
            [wrongReturn, wrongParameter, wrongCount]);

        Assert.Empty(bindings);
    }

    // A static method with one parameter more than the Invoke, a reference, closes over its first
    // argument (ECMA-335 II.14.6.1): an extension method's group, text.Twice (issue 223's class).
    [Fact]
    public void PlannerBindsAStaticClosedOverItsFirstReferenceArgument()
    {
        var reference = Type("Reference");
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var planner = new[]
        {
            new DelegateBindingPlanner(new RecordingRelationships(), new ManagedMethodIdentityFactory()),
        }.Cast<IDelegateBindingPlanner>().Single();
        var invoke = Method("Invoke", Type("Callback"), scalar, scalar);
        var closed = Method("Closed", Type("Owner1"), scalar, reference, scalar);
        var scalarFirst = Method("ScalarFirst", Type("Owner2"), scalar, scalar, scalar);
        var instance = Method("Instance", Type("Owner3"), scalar, reference, scalar);
        instance = instance with { Definition = instance.Definition with { IsStatic = false } };

        var binding = Assert.Single(planner.Plan([invoke], [closed, scalarFirst, instance]));

        Assert.Same(closed, binding.Target);
        Assert.True(binding.ClosedOverFirstArgument);
        Assert.True(binding.PassesTarget);
        Assert.Equal(scalar, Assert.Single(binding.ParameterBindings).TargetType);
    }

    [Fact]
    public void PlannerDeduplicatesAndOrdersInvokesAndTargets()
    {
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var invokeB = Method("Invoke", Type("CallbackB"), scalar);
        var invokeA = Method("Invoke", Type("CallbackA"), scalar);
        var targetB = Method("TargetB", Type("OwnerB"), scalar);
        var targetA = Method("TargetA", Type("OwnerA"), scalar);
        var planner = new[]
        {
            new DelegateBindingPlanner(
                new RecordingRelationships(),
                new ManagedMethodIdentityFactory()),
        }.Cast<IDelegateBindingPlanner>().Single();

        var bindings = planner.Plan(
            [invokeB, invokeA, invokeB],
            [targetB, targetA, targetB]);

        Assert.Equal(4, bindings.Length);
        Assert.Equal(
            [invokeA.CanonicalName, invokeA.CanonicalName,
                invokeB.CanonicalName, invokeB.CanonicalName],
            bindings.Select(binding => binding.InvokeIdentity.CanonicalName));
        Assert.Equal(
            [targetA.CanonicalName, targetB.CanonicalName,
                targetA.CanonicalName, targetB.CanonicalName],
            bindings.Select(binding => binding.TargetIdentity.CanonicalName));
    }

    [Fact]
    public void PlannerRejectsMissingDependenciesAndInputs()
    {
        var relationships = new RecordingRelationships();
        var identities = new ManagedMethodIdentityFactory();

        Assert.Throws<ArgumentNullException>(() =>
            new DelegateBindingPlanner(null!, identities));
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateBindingPlanner(relationships, null!));

        var planner = new[]
        {
            new DelegateBindingPlanner(relationships, identities),
        }.Cast<IDelegateBindingPlanner>().Single();
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, []));
        Assert.Throws<ArgumentNullException>(() => planner.Plan([], null!));
    }

    private static CliTypeIdentity Type(string name) =>
        CliTypeIdentity.Named(Assembly, "DelegateTests", name, isValueType: false);

    private static MethodInstanceModel Method(
        string name,
        CliTypeIdentity declaringType,
        CliTypeIdentity returnType,
        params CliTypeIdentity[] parameters)
    {
        var token = 0x06000000 + name.Aggregate(
            17,
            (value, character) => unchecked((value * 31) + character));
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, token),
            TypeKey,
            name,
            true,
            new MethodSignatureModel(returnType, [.. parameters]),
            1);
        return new(definition, declaringType, [], definition.Signature);
    }

    private sealed class RecordingRelationships(
        params (CliTypeIdentity Source, CliTypeIdentity Target)[] assignable) :
        ITypeRelationshipClassifier
    {
        private readonly HashSet<(CliTypeIdentity Source, CliTypeIdentity Target)>
            _assignable = [.. assignable];

        public List<(CliTypeIdentity Source, CliTypeIdentity Target)> Requests { get; } = [];

        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target)
        {
            Requests.Add((candidate, target));
            return new((_assignable.Contains((candidate, target))
                    ? TypeRelationshipCharacteristics.Hierarchy
                    : TypeRelationshipCharacteristics.None));
        }
    }
}
