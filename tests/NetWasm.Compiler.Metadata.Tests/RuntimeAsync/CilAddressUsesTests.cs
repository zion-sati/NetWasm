// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

public sealed class CilAddressUsesTests
{
    private static readonly AssemblyIdentity Assembly = new("addresses");
    private static readonly CliTypeIdentity Struct = CliTypeIdentity.Named(Assembly, "", "S", true);
    private static readonly CliTypeIdentity SpanLike = CliTypeIdentity.Named(Assembly, "", "SpanLike", true);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);

    // An address a call takes as its receiver and drops (an awaiter's IsCompleted, an awaitable's ConfigureAwait) reads
    // the local there: live up to the call, dead after its last such read.
    [Fact]
    public void AnAddressACallUsesAndDropsReadsTheLocalThere()
    {
        var (uses, liveness) = Analyze(
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(3, CilOperation.Call, Call("Add", isStatic: false, Int32, Int32)),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Return),
        ]);

        Assert.False(uses.ExposedLocals[0]);
        Assert.True(liveness.LocalLiveAfter(0, 0));
        Assert.False(liveness.LocalLiveAfter(3, 0));
    }

    // initobj, and a value type's constructor called on the address, write the whole local: it is dead before them,
    // though an argument the constructor reads from the local keeps it live up to there.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitobjAndAConstructorWriteTheWholeLocal(bool constructor)
    {
        ImmutableArray<CilInstruction> code = constructor
            ? [
                I(0, CilOperation.Nop),
                I(1, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(3, CilOperation.Call, Call(".ctor", isStatic: false, CliTypeIdentity.FromStackKind(CliValueKind.Void), Int32)),
                I(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(5, CilOperation.Pop),
                I(6, CilOperation.Return),
            ]
            : [
                I(0, CilOperation.Nop),
                I(1, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                I(2, CilOperation.InitializeObject, new CilOperand.TypeIdentity(Struct)),
                I(3, CilOperation.Nop),
                I(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(5, CilOperation.Pop),
                I(6, CilOperation.Return),
            ];
        var (uses, liveness) = Analyze(code);

        Assert.False(uses.ExposedLocals[0]);
        Assert.False(liveness.LocalLiveAfter(0, 0));
        Assert.True(liveness.LocalLiveAfter(3, 0));
        Assert.False(liveness.LocalLiveAfter(4, 0));
    }

    // An address that may outlive its consumer keeps its local live everywhere: given to a call that returns a
    // reference, a pointer or a by-reference-like value, or that takes a by-reference-like value; duplicated; offset by
    // ldflda; carried across a branch or into code another path reaches.
    [Theory]
    [InlineData("returns a reference")]
    [InlineData("returns a pointer")]
    [InlineData("returns a by-reference-like value")]
    [InlineData("takes a by-reference-like value")]
    [InlineData("duplicated")]
    [InlineData("ldflda")]
    [InlineData("branch")]
    [InlineData("entered")]
    public void AnAddressThatMayOutliveItsConsumerKeepsTheLocalLiveEverywhere(string escape)
    {
        var receiver = new CilInstruction[] { I(1, CilOperation.LoadLocalAddress, new CilOperand.Index(0)) };
        CilInstruction[] consumer = escape switch
        {
            "returns a reference" => [I(2, CilOperation.Call, Call("Ref", isStatic: false, CliTypeIdentity.ManagedByReference(Int32))), I(3, CilOperation.Pop)],
            "returns a pointer" => [I(2, CilOperation.Call, Call("Pointer", isStatic: false, CliTypeIdentity.UnmanagedPointer(Int32))), I(3, CilOperation.Pop)],
            "returns a by-reference-like value" => [I(2, CilOperation.Call, Call("Span", isStatic: false, SpanLike)), I(3, CilOperation.Pop)],
            "takes a by-reference-like value" =>
            [
                I(2, CilOperation.LoadLocal, new CilOperand.Index(1)),
                I(3, CilOperation.Call, Call("Fill", isStatic: false, CliTypeIdentity.FromStackKind(CliValueKind.Void), SpanLike)),
            ],
            "duplicated" => [I(2, CilOperation.Duplicate), I(3, CilOperation.Pop), I(4, CilOperation.Pop)],
            "ldflda" => [I(2, CilOperation.LoadFieldAddress), I(3, CilOperation.Pop)],
            "branch" =>
            [
                I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
                I(4, CilOperation.Call, Call("Read", isStatic: false, Int32)),
                I(5, CilOperation.Pop),
            ],
            _ =>
            [
                I(2, CilOperation.Call, Call("Read", isStatic: false, Int32)),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Nop),
                I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(6, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(2)),
            ],
        };
        var end = consumer[^1].Offset + 1;
        var (uses, liveness) = Analyze([I(0, CilOperation.Nop), .. receiver, .. consumer, I(end, CilOperation.Return)]);

        Assert.True(uses.ExposedLocals[0]);
        Assert.True(liveness.LocalLiveAfter(end, 0));
    }

    // An argument's address follows the same rules as a local's.
    [Fact]
    public void AnArgumentsAddressFollowsTheSameRules()
    {
        var (uses, liveness) = Analyze(
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
            I(2, CilOperation.Call, Call("Read", isStatic: false, Int32)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadArgumentAddress, new CilOperand.Index(1)),
            I(5, CilOperation.Call, Call("Ref", isStatic: false, CliTypeIdentity.ManagedByReference(Int32))),
            I(6, CilOperation.Pop),
            I(7, CilOperation.Return),
        ]);

        Assert.False(uses.ExposedArguments[0]);
        Assert.True(uses.ExposedArguments[1]);
        Assert.True(liveness.ArgumentLiveAfter(0, 0));
        Assert.False(liveness.ArgumentLiveAfter(2, 0));
        Assert.True(liveness.ArgumentLiveAfter(7, 1));
    }

    private static (CilAddressUses Uses, CilLiveness Liveness) Analyze(ImmutableArray<CilInstruction> instructions)
    {
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "M", true,
            new MethodSignatureModel(CliValueKind.Void, []), 0);
        var body = new CilMethodBody(definition, 4, [CliValueKind.I4, CliValueKind.I4], instructions);
        var uses = CilAddressUses.Compute(body, 2, type => type.Equals(SpanLike));
        return (uses, CilLiveness.Compute(body, 2, uses));
    }

    private static CilOperand.MethodInstance Call(string name, bool isStatic, CliTypeIdentity returned,
        params CliTypeIdentity[] parameters)
    {
        var signature = new MethodSignatureModel(returned, [.. parameters]);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), name, isStatic,
            signature, 1);
        return new CilOperand.MethodInstance(new(definition, Struct, [], signature));
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());
}
