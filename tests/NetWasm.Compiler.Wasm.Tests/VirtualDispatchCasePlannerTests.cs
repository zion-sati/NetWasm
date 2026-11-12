using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

namespace NetWasm.Compiler.Wasm.Tests;

// A dispatch emits each target's code once for every run of receiver type ids it serves, so
// targets whose code does not depend on the receiver share a case (issue 222).
public sealed class VirtualDispatchCasePlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("Dispatch.Tests");
    private static readonly CliTypeIdentity Enum = Reference("System", "Enum");
    private static readonly CliTypeIdentity ValueType = Reference("System", "ValueType");
    private static readonly CliTypeIdentity Int32 = Value("System", "Int32");
    private static readonly MethodInstanceModel EnumToInt64 = Method(0x06000001, Enum);
    private static readonly MethodInstanceModel Int32ToInt64 = Method(0x06000002, Int32);
    private static readonly MethodInstanceModel ValueTypeEquals = Method(0x06000003, ValueType);

    // Every enum's IConvertible is Enum's, whose intrinsic tests the enum type itself: one case
    // spans the enums, over ids no receiver of the site has.
    [Fact]
    public void ReceiversSharingCodeAreOneRunAcrossIdsNoReceiverHas()
    {
        int[] ids = [10, 11, 13, 15];
        var enums = ids.Select(id => new DispatchTargetModel(Value("Enums", $"E{id}"), EnumToInt64)).ToArray();
        var int32 = new DispatchTargetModel(Int32, Int32ToInt64);
        ImmutableArray<DispatchTargetModel> receivers = [.. enums, int32];

        var cases = Planner(receivers.Select(target => target.ReceiverType)
            .Zip<CliTypeIdentity, int>([10, 11, 13, 15, 3])).Plan(receivers, receivers);

        VirtualDispatchCase[] expected = [new(int32, 3, 3), new(enums[0], 10, 15)];
        Assert.Equal(expected, cases);
    }

    // A receiver between two of a case's receivers, checked or left to the fallback, needs its
    // own target, so the run ends before it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnotherReceiverBetweenEndsTheRun(bool checkedReceiver)
    {
        var inherited = Method(0x06000004, Reference("Dispatch", "Base"));
        var first = new DispatchTargetModel(Reference("Dispatch", "First"), inherited);
        var between = new DispatchTargetModel(Reference("Dispatch", "Between"), Method(0x06000005, Reference("Dispatch", "Between")));
        var last = new DispatchTargetModel(Reference("Dispatch", "Last"), inherited);
        ImmutableArray<DispatchTargetModel> receivers = [first, between, last];
        var planner = Planner(receivers.Select(target => target.ReceiverType).Zip<CliTypeIdentity, int>([30, 31, 32]));

        var cases = planner.Plan(checkedReceiver ? receivers : [first, last], receivers);

        VirtualDispatchCase[] expected = checkedReceiver
            ? [new(first, 30, 30), new(between, 31, 31), new(last, 32, 32)]
            : [new(first, 30, 30), new(last, 32, 32)];
        Assert.Equal(expected, cases);
    }

    // A value type's Equals and GetHashCode intrinsics read its fields, and a method a value
    // type declares takes its payload address: each receiver keeps its own case.
    [Theory]
    [InlineData(RuntimeIntrinsic.ValueTypeEquals, false)]
    [InlineData(RuntimeIntrinsic.ValueTypeGetHashCode, false)]
    [InlineData(null, true)]
    public void CodeThatDependsOnTheReceiverStaysWithIt(RuntimeIntrinsic? intrinsic, bool declaredOnValueType)
    {
        var method = declaredOnValueType ? Method(0x06000006, Value("Dispatch", "Pair")) : ValueTypeEquals;
        var first = new DispatchTargetModel(Value("Dispatch", "First"), method);
        var second = new DispatchTargetModel(Value("Dispatch", "Second"), method);
        ImmutableArray<DispatchTargetModel> receivers = [first, second];
        var planner = new VirtualDispatchCasePlanner(
            new Layouts(receivers.Select(target => target.ReceiverType).Zip<CliTypeIdentity, int>([20, 21])),
            new Intrinsics(intrinsic is { } value ? new() { [method.Definition.Key] = value } : new()));

        VirtualDispatchCase[] expected = [new(first, 20, 20), new(second, 21, 21)];
        Assert.Equal(expected, planner.Plan(receivers, receivers));
    }

    [Fact]
    public void AReceiverListedTwiceIsOneMember()
    {
        var target = new DispatchTargetModel(Value("Enums", "E1"), EnumToInt64);

        var cases = Planner([(target.ReceiverType, 10)]).Plan([target, target], [target, target]);

        Assert.Equal(new VirtualDispatchCase(target, 10, 10), Assert.Single(cases));
    }

    [Fact]
    public void RejectsMissingCollaborators()
    {
        Assert.Equal("typeLayouts", Assert.Throws<ArgumentNullException>(() =>
            new VirtualDispatchCasePlanner(null!, new Intrinsics(new()))).ParamName);
        Assert.Equal("intrinsics", Assert.Throws<ArgumentNullException>(() =>
            new VirtualDispatchCasePlanner(new Layouts([]), null!)).ParamName);
    }

    private static VirtualDispatchCasePlanner Planner(IEnumerable<(CliTypeIdentity Type, int Id)> ids) =>
        new(new Layouts(ids), new Intrinsics(new() { [EnumToInt64.Definition.Key] = RuntimeIntrinsic.EnumConvert }));

    private static MethodInstanceModel Method(int token, CliTypeIdentity declaringType)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I8);
        var definition = new MethodDefinitionModel(new(Assembly, token), new(Assembly, 0x02000001), "ToInt64", false, signature, 1)
        {
            IsVirtual = true,
        };
        return new(definition, declaringType, [], signature);
    }

    private static CliTypeIdentity Reference(string space, string name) =>
        CliTypeIdentity.Named(Assembly, space, name, isValueType: false);

    private static CliTypeIdentity Value(string space, string name) =>
        CliTypeIdentity.Named(Assembly, space, name, isValueType: true);

    private sealed class Layouts(IEnumerable<(CliTypeIdentity Type, int Id)> ids) : ITypeLayoutProvider
    {
        private readonly Dictionary<CliTypeIdentity, int> _ids = ids.ToDictionary(item => item.Type, item => item.Id);

        public int StringTypeId => throw new NotSupportedException();
        public int TypeTypeId => throw new NotSupportedException();

        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => new(_ids[type], 16, []);

        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout) => throw new NotSupportedException();

        public ObjectLayout GetObjectLayout(EntityKey type) => throw new NotSupportedException();
    }

    private sealed class Intrinsics(Dictionary<EntityKey, RuntimeIntrinsic> intrinsics) : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
            intrinsics.TryGetValue(method, out intrinsic);
    }
}
