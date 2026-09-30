using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Tests.ExecutionPlannerFixture;

namespace NetWasm.Compiler.Tests;

public sealed class ObjectArrayDelegateAdapterPlannerTests
{
    [Fact]
    public void PlanRejectsNullDependenciesAndInput()
    {
        var f = new ExecutionPlannerFixture();
        Assert.Throws<ArgumentNullException>(() => new ObjectArrayDelegateAdapterPlanner(null!, f, f, f, f, f, f));
        Assert.Throws<ArgumentNullException>(() => new ObjectArrayDelegateAdapterPlanner(f, null!, f, f, f, f, f));
        Assert.Throws<ArgumentNullException>(() => new ObjectArrayDelegateAdapterPlanner(f, f, null!, f, f, f, f));
        Assert.Throws<ArgumentNullException>(() => new ObjectArrayDelegateAdapterPlanner(f, f, f, null!, f, f, f));
        Assert.Throws<ArgumentNullException>(() => new ObjectArrayDelegateAdapterPlanner(f, f, f, f, null!, f, f));
        Assert.Throws<ArgumentNullException>(() => new ObjectArrayDelegateAdapterPlanner(f, f, f, f, f, null!, f));
        Assert.Throws<ArgumentNullException>(() => new ObjectArrayDelegateAdapterPlanner(f, f, f, f, f, f, null!));
        Assert.Throws<ArgumentNullException>(() => f.AdapterPlanner().Plan(null!));
        Assert.Empty(f.Lookups);
        Assert.Empty(f.Resolutions);
    }

    [Fact]
    public void PlanWithoutFactoryDemandDoesNotResolveSupport()
    {
        var f = new ExecutionPlannerFixture();
        var unrelated = f.Demand(2, RuntimeIntrinsic.MemberReadField);
        var ordinary = f.Method(3, "Ordinary");

        Assert.Empty(f.AdapterPlanner().Plan([]));
        Assert.Empty(f.AdapterPlanner().Plan([unrelated, ordinary]));
        Assert.Empty(f.Lookups);
        Assert.Empty(f.Resolutions);
        Assert.Empty(f.Recognitions);
        Assert.Empty(f.DefinitionLookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlanBindsCustomAndClosedGenericUnaryDelegates(bool generic)
    {
        var f = new ExecutionPlannerFixture();
        var result = CliTypeIdentity.FromStackKind(CliValueKind.F8);
        var type = generic ? CliTypeIdentity.GenericInstantiation(Type("Callback`2"), [Scalar, result]) : Type("Callback");
        var factory = f.Factory(type);
        var invoke = f.Method(2, "Invoke", type, result, parameters: [Scalar]);
        var irrelevantName = f.Method(3, "Other", type);
        var irrelevantStatic = f.Method(4, "Invoke", type, isStatic: true);
        f.RegisterDelegate(type, irrelevantName, invoke, irrelevantStatic);
        var target = f.UnarySupport();

        var plan = Assert.Single(f.AdapterPlanner().Plan([factory])).Value;

        Assert.True(plan.IsSupported);
        Assert.Same(factory, plan.Factory);
        Assert.Same(type, plan.DelegateType);
        Assert.Same(invoke, plan.Invoke);
        Assert.Same(target, plan.Target);
        Assert.Equal(["System.Runtime.CompilerServices.ObjectArrayDelegateTarget"], f.Lookups);
        Assert.Equal(2, f.Resolutions.Count);
        Assert.Equal(generic ? new[] { Scalar, result } : [], f.Resolutions[0].Context!.Value.TypeArguments);
        Assert.Empty(f.Resolutions[0].Context!.Value.MethodArguments);
        Assert.Empty(f.Resolutions[1].Context!.Value.TypeArguments);
        Assert.Equal([Scalar, result], f.Resolutions[1].Context!.Value.MethodArguments.ToArray());
    }

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(2, false, false, false)]
    [InlineData(1, true, false, false)]
    [InlineData(1, false, true, false)]
    [InlineData(1, false, false, true)]
    public void UnsupportedSignaturesSelectExplicitThrowTarget(int count, bool byrefParameter, bool voidResult, bool byrefResult)
    {
        var f = new ExecutionPlannerFixture();
        var type = Type("Unsupported");
        var factory = f.Factory(type);
        var parameter = byrefParameter ? CliTypeIdentity.ManagedByReference(Scalar) : Scalar;
        var result = voidResult ? CliTypeIdentity.FromStackKind(CliValueKind.Void) : byrefResult ? CliTypeIdentity.ManagedByReference(Scalar) : Scalar;
        var invoke = f.Method(2, "Invoke", type, result, parameters: [.. Enumerable.Repeat(parameter, count)]);
        f.RegisterDelegate(type, invoke);
        var target = f.UnsupportedSupport();

        var plan = Assert.Single(f.AdapterPlanner().Plan([factory])).Value;

        Assert.False(plan.IsSupported);
        Assert.Same(factory, plan.Factory);
        Assert.Same(invoke, plan.Invoke);
        Assert.Same(target, plan.Target);
        Assert.Equal(["System.Runtime.CompilerServices.ObjectArrayDelegateAdapter"], f.Lookups);
        Assert.Null(f.Resolutions[1].Context);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsFactoryWithoutExactlyOneTypeArgument(int count)
    {
        var f = new ExecutionPlannerFixture();
        var factory = f.Factory(Type("Callback")) with { MethodArguments = [.. Enumerable.Repeat(Scalar, count)] };
        AssertContract(f, factory);
        Assert.Empty(f.Recognitions);
        Assert.Empty(f.DefinitionLookups);
    }

    [Fact]
    public void RejectsOpenDelegateArgumentBeforeMetadataLookup()
    {
        var f = new ExecutionPlannerFixture();
        var type = CliTypeIdentity.GenericParameter(false, 0);
        var factory = f.Factory(type);
        AssertContract(f, factory);
        Assert.Empty(f.Recognitions);
        Assert.Empty(f.DefinitionLookups);
    }

    [Theory]
    [InlineData("Object", false)]
    [InlineData("Int32", true)]
    [InlineData("Delegate", false)]
    [InlineData("MulticastDelegate", false)]
    public void InvalidDelegateTypesSelectArgumentThrowTarget(
        string name,
        bool valueType)
    {
        var f = new ExecutionPlannerFixture();
        var type = CliTypeIdentity.Named(Assembly, "System", name, valueType);
        if (name is "Delegate" or "MulticastDelegate")
        {
            f.Delegates.Add(type);
        }
        var factory = f.Factory(type);
        var target = f.InvalidDelegateSupport();

        var plan = Assert.Single(f.AdapterPlanner().Plan([factory])).Value;

        Assert.False(plan.IsSupported);
        Assert.Null(plan.Invoke);
        Assert.Same(target, plan.Target);
        Assert.Empty(f.DefinitionLookups);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsMissingOrAmbiguousInstanceInvoke(int count)
    {
        var f = new ExecutionPlannerFixture();
        var type = Type("Callback");
        var invokes = Enumerable.Range(2, count).Select(row => f.Method(row, "Invoke", type)).ToArray();
        f.RegisterDelegate(type, invokes);
        AssertContract(f, f.Factory(type));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    public void RejectsMissingOrAmbiguousSupportMethodsWithRuntimeContract(bool supported, int count)
    {
        var f = new ExecutionPlannerFixture();
        var type = Type("Callback");
        var factory = f.Factory(type);
        var invoke = f.Method(2, "Invoke", type, parameters: supported ? [Scalar] : []);
        f.RegisterDelegate(type, invoke);
        var supportName = supported ? "Invoke1" : "ThrowUnsupported";
        var candidates = Enumerable.Range(10, count).Select(row => f.Method(row, supportName,
            isStatic: !supported, genericArity: supported ? 2 : 0, parameters: supported ? [Scalar] : [])).ToArray();
        f.RegisterType(supported
            ? "System.Runtime.CompilerServices.ObjectArrayDelegateTarget"
            : "System.Runtime.CompilerServices.ObjectArrayDelegateAdapter", candidates);

        var exception = Assert.Throws<CompilerException>(() => f.AdapterPlanner().Plan([factory]));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
        Assert.Single(f.Resolutions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SupportSelectionIgnoresMethodsWithWrongMetadata(bool supported)
    {
        var f = new ExecutionPlannerFixture();
        var type = Type("Callback");
        var factory = f.Factory(type);
        f.RegisterDelegate(type, f.Method(2, "Invoke", type, parameters: supported ? [Scalar] : []));
        var target = supported ? f.UnarySupport() : f.UnsupportedSupport();
        var name = supported ? "Invoke1" : "ThrowUnsupported";
        var wrongName = f.Method(10, "Other", isStatic: !supported, genericArity: 2, parameters: supported ? [Scalar] : []);
        var wrongStatic = f.Method(11, name, isStatic: supported, genericArity: 2, parameters: supported ? [Scalar] : []);
        var wrongCount = f.Method(12, name, isStatic: !supported, genericArity: 2, parameters: supported ? [] : [Scalar]);
        var wrongArity = f.Method(13, supported ? name : "Other", isStatic: !supported, genericArity: 1, parameters: supported ? [Scalar] : []);
        f.RegisterType(supported
            ? "System.Runtime.CompilerServices.ObjectArrayDelegateTarget"
            : "System.Runtime.CompilerServices.ObjectArrayDelegateAdapter", wrongName, wrongStatic, wrongCount, wrongArity, target);

        Assert.Same(target, Assert.Single(f.AdapterPlanner().Plan([factory])).Value.Target);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsMissingOrAmbiguousInvalidDelegateSupportWithRuntimeContract(
        int count)
    {
        var f = new ExecutionPlannerFixture();
        var factory = f.Factory(Type("NotDelegate"));
        var candidates = Enumerable.Range(10, count)
            .Select(row => f.Method(row, "ThrowInvalidDelegate", isStatic: true))
            .ToArray();
        f.RegisterType(
            "System.Runtime.CompilerServices.ObjectArrayDelegateAdapter",
            candidates);

        var exception = Assert.Throws<CompilerException>(() =>
            f.AdapterPlanner().Plan([factory]));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
        Assert.Empty(f.Resolutions);
    }

    [Fact]
    public void PlanIsIndependentOfReachabilityEnumerationOrder()
    {
        var f = new ExecutionPlannerFixture();
        var type = Type("Callback");
        var first = f.Factory(type);
        var second = f.Demand(3, RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate) with { MethodArguments = [type] };
        f.RegisterDelegate(type, f.Method(2, "Invoke", type, parameters: [Scalar]));
        f.UnarySupport();
        var planner = f.AdapterPlanner();

        var reversed = planner.Plan([second, first]);
        var ordered = planner.Plan([first, second]);

        Assert.Equal(2, ordered.Count);
        Assert.Equal(ordered.OrderBy(pair => pair.Key), reversed.OrderBy(pair => pair.Key));
        Assert.Same(StringComparer.Ordinal, ordered.KeyComparer);
    }

    private static void AssertContract(ExecutionPlannerFixture fixture, MethodInstanceModel factory)
    {
        var exception = Assert.Throws<CompilerException>(() => fixture.AdapterPlanner().Plan([factory]));
        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
        Assert.Empty(fixture.Lookups);
        Assert.Empty(fixture.Resolutions);
    }
}
