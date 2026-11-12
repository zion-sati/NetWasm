using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class IntrinsicAddressBodyPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("CoreLib");

    [Fact]
    public void ForwardsAnInstanceIntrinsicsReceiverAndArgumentsToItself()
    {
        var method = Instance("HasFlag", isStatic: false, CliValueKind.I4, CliValueKind.ManagedReference);

        var request = new IntrinsicAddressBodyPlanner(new Intrinsics(method.Definition.Key)).Plan(method);

        Assert.NotNull(request);
        var body = Assert.IsType<CilMethodBody>(request.RewrittenBody);
        Assert.Same(method, request.Method);
        Assert.Same(method, body.MethodInstance);
        Assert.Equal(
            [CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.Call, CilOperation.Return],
            body.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal([0, 1], body.Instructions.Take(2).Select(instruction => ((CilOperand.Index)instruction.Operand).Value));
        Assert.Same(method, ((CilOperand.MethodInstance)body.Instructions[2].Operand).Value);
        Assert.Equal(2, body.MaxStack);
    }

    [Fact]
    public void ForwardsAStaticIntrinsicsArgumentsOnly()
    {
        var method = Instance("Abs", isStatic: true, CliValueKind.F8, CliValueKind.F8);

        var body = new IntrinsicAddressBodyPlanner(new Intrinsics(method.Definition.Key)).Plan(method)?.RewrittenBody;

        Assert.NotNull(body);
        Assert.Equal(
            [CilOperation.LoadArgument, CilOperation.Call, CilOperation.Return],
            body.Instructions.Select(instruction => instruction.Operation));
    }

    // CoreLib's body for an intrinsic is often a stand-in only call sites replace (Object.GetType
    // returned null, string.Length 0, Unsafe.As threw), so an address forwards to the intrinsic
    // even where the method has CIL (issue 223's class).
    [Fact]
    public void ForwardsAnIntrinsicWithCilToItselfRatherThanRunningItsBody()
    {
        var method = Instance("HasFlag", isStatic: false, CliValueKind.I4, CliValueKind.ManagedReference, relativeVirtualAddress: 0x2050);

        var request = new IntrinsicAddressBodyPlanner(new Intrinsics(method.Definition.Key)).Plan(method);

        Assert.NotNull(request);
        Assert.Same(method, request.Method);
        var body = Assert.IsType<CilMethodBody>(request.RewrittenBody);
        Assert.Same(method, ((CilOperand.MethodInstance)body.Instructions[2].Operand).Value);
    }

    [Fact]
    public void LeavesOrdinaryMethodsToTheirOwnBodies()
    {
        var method = Instance("Run", isStatic: true, CliValueKind.Void);

        Assert.Null(new IntrinsicAddressBodyPlanner(new Intrinsics()).Plan(method));
    }

    [Fact]
    public void RejectsMissingCollaboratorsAndMethods()
    {
        Assert.Throws<ArgumentNullException>(() => new IntrinsicAddressBodyPlanner(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new IntrinsicAddressBodyPlanner(new Intrinsics()).Plan(null!));
    }

    private static MethodInstanceModel Instance(
        string name, bool isStatic, CliValueKind returnType, params CliValueKind[] parameters) =>
        Instance(name, isStatic, returnType, parameters, relativeVirtualAddress: 0);

    private static MethodInstanceModel Instance(
        string name, bool isStatic, CliValueKind returnType, CliValueKind parameter, int relativeVirtualAddress) =>
        Instance(name, isStatic, returnType, [parameter], relativeVirtualAddress);

    private static MethodInstanceModel Instance(
        string name, bool isStatic, CliValueKind returnType, CliValueKind[] parameters, int relativeVirtualAddress)
    {
        var signature = MethodSignatureModel.Create(returnType, parameters);
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000001), new EntityKey(Assembly, 0x02000001), name, isStatic, signature,
            relativeVirtualAddress);
        return new(definition, CliTypeIdentity.Named(Assembly, "System", "Owner", isValueType: false), [], signature);
    }

    private sealed class Intrinsics(params EntityKey[] methods) : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = RuntimeIntrinsic.EnumHasFlag;
            return methods.Contains(method);
        }
    }
}
