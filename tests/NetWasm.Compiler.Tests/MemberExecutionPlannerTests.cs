using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using static NetWasm.Compiler.Tests.ExecutionPlannerFixture;

namespace NetWasm.Compiler.Tests;

public sealed class MemberExecutionPlannerTests
{
    [Fact]
    public void PlanRejectsNullDependenciesAndInputs()
    {
        var f = new ExecutionPlannerFixture();
        Assert.Throws<ArgumentNullException>(() => new MemberExecutionPlanner(null!, f, f, f, f, f));
        Assert.Throws<ArgumentNullException>(() => new MemberExecutionPlanner(f, null!, f, f, f, f));
        Assert.Throws<ArgumentNullException>(() => new MemberExecutionPlanner(f, f, null!, f, f, f));
        Assert.Throws<ArgumentNullException>(() => new MemberExecutionPlanner(f, f, f, null!, f, f));
        Assert.Throws<ArgumentNullException>(() => new MemberExecutionPlanner(f, f, f, f, null!, f));
        Assert.Throws<ArgumentNullException>(() => new MemberExecutionPlanner(f, f, f, f, f, null!));
        var planner = f.MemberPlanner();
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, [], []));
        Assert.Throws<ArgumentNullException>(() => planner.Plan([], null!, []));
        Assert.Throws<ArgumentNullException>(() => planner.Plan([], [], null!));
        Assert.Empty(f.Lookups);
        Assert.Empty(f.Resolutions);
    }

    [Fact]
    public void PlanWithoutMemberDemandDoesNotEnumerateDescriptorsOrResolveSupport()
    {
        var f = new ExecutionPlannerFixture();
        var unrelated = f.Demand(1, RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate);
        var ordinary = f.Method(2, "Ordinary");
        var planner = f.MemberPlanner();

        Assert.Same(MemberExecutionPlan.Empty, planner.Plan([], NeverEnumerate<MethodInstanceModel>(), NeverEnumerate<FieldInstanceModel>()));
        Assert.Same(MemberExecutionPlan.Empty, planner.Plan([unrelated, ordinary], NeverEnumerate<MethodInstanceModel>(), NeverEnumerate<FieldInstanceModel>()));
        Assert.Empty(f.Lookups);
        Assert.Empty(f.Resolutions);
        Assert.Empty(f.Recognitions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlanOnlyEnumeratesDemandedDescriptorKind(bool methodDemand)
    {
        var f = new ExecutionPlannerFixture();
        var demand = f.Demand(1, methodDemand ? RuntimeIntrinsic.MemberExecuteMethod : RuntimeIntrinsic.MemberReadField);
        var target = f.UnsupportedSupport(member: true);
        var method = f.Method(2, "Get");
        var field = Field(1, Scalar);

        var plan = f.MemberPlanner().Plan([demand],
            methodDemand ? [method] : NeverEnumerate<MethodInstanceModel>(),
            methodDemand ? NeverEnumerate<FieldInstanceModel>() : [field]);

        Assert.Equal(methodDemand ? 1 : 0, plan.Methods.Count);
        Assert.Equal(methodDemand ? 0 : 1, plan.Fields.Count);
        Assert.Same(target, plan.UnsupportedTarget);
        Assert.Equal(["System.Runtime.CompilerServices.RuntimeMemberExecution"], f.Lookups);
        Assert.Null(Assert.Single(f.Resolutions).Context);
    }

    [Theory]
    [InlineData(CliValueKind.ManagedReference)]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.I8)]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    public void PlanIncludesSupportedResultsAndPreservesDispatchRequirement(CliValueKind kind)
    {
        var f = new ExecutionPlannerFixture();
        var execute = f.Demand(1, RuntimeIntrinsic.MemberExecuteMethod);
        var read = f.Demand(2, RuntimeIntrinsic.MemberReadField);
        var result = CliTypeIdentity.FromStackKind(kind);
        var direct = f.Method(3, "Direct", result: result);
        var virtualMethod = f.Method(4, "Virtual", result: result, isVirtual: true);
        var abstractMethod = f.Method(5, "Abstract", result: result, isAbstract: true, isVirtual: true);
        var field = Field(1, result);
        var target = f.UnsupportedSupport(member: true);

        var plan = f.MemberPlanner().Plan([execute, read], [direct, virtualMethod, abstractMethod], [field]);

        Assert.Equal(3, plan.Methods.Count);
        Assert.Same(direct, plan.Methods[direct.CanonicalName].Descriptor);
        Assert.False(plan.Methods[direct.CanonicalName].RequiresDispatch);
        Assert.True(plan.Methods[virtualMethod.CanonicalName].RequiresDispatch);
        Assert.True(plan.Methods[abstractMethod.CanonicalName].RequiresDispatch);
        Assert.All(plan.Methods.Values, method => Assert.Empty(method.Targets));
        Assert.Same(field, Assert.Single(plan.Fields).Value);
        Assert.Same(target, plan.UnsupportedTarget);
    }

    [Theory]
    [InlineData(CliValueKind.ManagedReference)]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.I8)]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    public void PlanIncludesStaticMethodsAndSupportedParameters(CliValueKind kind)
    {
        var f = new ExecutionPlannerFixture();
        var execute = f.Demand(1, RuntimeIntrinsic.MemberExecuteMethod);
        var parameter = CliTypeIdentity.FromStackKind(kind);
        var method = f.Method(
            3,
            "Map",
            Type("StaticOwner", valueType: true),
            isStatic: true,
            parameters: [parameter]);
        f.UnsupportedSupport(member: true);

        var plan = f.MemberPlanner().Plan([execute], [method], []);

        Assert.Same(method, Assert.Single(plan.Methods).Value.Descriptor);
    }

    [Theory]
    [InlineData(CliValueKind.Void)]
    [InlineData(CliValueKind.ManagedAddress)]
    [InlineData(CliValueKind.NativeInt)]
    [InlineData(CliValueKind.ValueType)]
    [InlineData(CliValueKind.Unknown)]
    public void PlanExcludesUnsupportedResults(CliValueKind kind)
    {
        var f = new ExecutionPlannerFixture();
        var execute = f.Demand(1, RuntimeIntrinsic.MemberExecuteMethod);
        var read = f.Demand(2, RuntimeIntrinsic.MemberReadField);
        var result = CliTypeIdentity.FromStackKind(kind);
        var method = f.Method(3, "Get", result: result);
        var field = Field(1, result);
        var target = f.UnsupportedSupport(member: true);

        var plan = f.MemberPlanner().Plan([execute, read], [method], [field]);

        Assert.Empty(plan.Methods);
        Assert.Empty(plan.Fields);
        Assert.Same(target, plan.UnsupportedTarget);
    }

    [Fact]
    public void PlanExcludesUnsupportedOwnersSignaturesIntrinsicsAndDelegateInvokeDescriptors()
    {
        var f = new ExecutionPlannerFixture();
        var execute = f.Demand(1, RuntimeIntrinsic.MemberExecuteMethod);
        var read = f.Demand(2, RuntimeIntrinsic.MemberReadField);
        var valueOwner = Type("Struct", valueType: true);
        var delegateOwner = Type("Callback");
        f.Delegates.Add(delegateOwner);
        var abstractNonVirtual = f.Method(4, "Abstract", isAbstract: true);
        var valueMethod = f.Method(5, "Get", valueOwner);
        var delegateInvoke = f.Method(6, "Invoke", delegateOwner);
        var unsupportedParameter = f.Method(
            7,
            "Get",
            parameters: [CliTypeIdentity.FromStackKind(CliValueKind.ValueType)]);
        var intrinsic = f.Method(8, "Intrinsic");
        f.Intrinsics.Add(intrinsic.Definition.Key, RuntimeIntrinsic.StringLength);
        var staticField = Field(1, Scalar, isStatic: true);
        var valueField = Field(2, Scalar, valueOwner);
        var target = f.UnsupportedSupport(member: true);

        var plan = f.MemberPlanner().Plan([execute, read],
            [abstractNonVirtual, valueMethod, delegateInvoke, unsupportedParameter, intrinsic],
            [staticField, valueField]);

        Assert.Empty(plan.Methods);
        Assert.Empty(plan.Fields);
        Assert.Same(target, plan.UnsupportedTarget);
        Assert.Contains(delegateOwner, f.Recognitions);
    }

    [Fact]
    public void PlanDeduplicatesDescriptorsAndIsIndependentOfEnumerationOrder()
    {
        var f = new ExecutionPlannerFixture();
        var execute = f.Demand(1, RuntimeIntrinsic.MemberExecuteMethod);
        var read = f.Demand(2, RuntimeIntrinsic.MemberReadField);
        var first = f.Method(3, "First");
        var second = f.Method(4, "Second");
        var firstField = Field(1, Scalar);
        var secondField = Field(2, Scalar);
        f.UnsupportedSupport(member: true);
        var planner = f.MemberPlanner();

        var reversed = planner.Plan([read, execute, read], [second, first, second], [secondField, firstField, secondField]);
        var ordered = planner.Plan([execute, read], [first, second], [firstField, secondField]);

        Assert.Equal(2, ordered.Methods.Count);
        Assert.Equal(2, ordered.Fields.Count);
        Assert.Equal(ordered.Methods.OrderBy(pair => pair.Key), reversed.Methods.OrderBy(pair => pair.Key));
        Assert.Equal(ordered.Fields.OrderBy(pair => pair.Key), reversed.Fields.OrderBy(pair => pair.Key));
        Assert.Same(StringComparer.Ordinal, ordered.Methods.KeyComparer);
        Assert.Same(StringComparer.Ordinal, ordered.Fields.KeyComparer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsMissingOrAmbiguousSupportWithRuntimeContract(int count)
    {
        var f = new ExecutionPlannerFixture();
        var demand = f.Demand(1, RuntimeIntrinsic.MemberExecuteMethod);
        var candidates = Enumerable.Range(10, count).Select(row => f.Method(row, "ThrowUnsupported", isStatic: true)).ToArray();
        f.RegisterType("System.Runtime.CompilerServices.RuntimeMemberExecution", candidates);

        var exception = Assert.Throws<CompilerException>(() => f.MemberPlanner().Plan([demand], [], []));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
        Assert.Empty(f.Resolutions);
    }

    [Fact]
    public void SupportSelectionRequiresStaticParameterlessNamedMethod()
    {
        var f = new ExecutionPlannerFixture();
        var demand = f.Demand(1, RuntimeIntrinsic.MemberReadField);
        var wrongName = f.Method(10, "Other", isStatic: true);
        var wrongStatic = f.Method(11, "ThrowUnsupported");
        var wrongParameters = f.Method(12, "ThrowUnsupported", isStatic: true, parameters: [Scalar]);
        var target = f.UnsupportedSupport(member: true);
        f.RegisterType("System.Runtime.CompilerServices.RuntimeMemberExecution", wrongName, wrongStatic, wrongParameters, target);

        Assert.Same(target, f.MemberPlanner().Plan([demand], [], []).UnsupportedTarget);
        Assert.Equal(target.Definition.Key, Assert.Single(f.Resolutions).Key);
    }

    private static FieldInstanceModel Field(int row, CliTypeIdentity type, CliTypeIdentity? owner = null, bool isStatic = false)
    {
        var definition = new FieldDefinitionModel(new(Assembly, 0x04000000 + row), new(Assembly, 0x02000001), $"Field{row}", type, isStatic);
        return new(definition, owner ?? Type("Owner"), type);
    }

    private static IEnumerable<T> NeverEnumerate<T>() =>
        Enumerable.Range(0, 1).Select<int, T>(_ =>
            throw new InvalidOperationException("Undemanded descriptors must not be enumerated."));
}
