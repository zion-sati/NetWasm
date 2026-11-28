// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

public sealed class CilLivenessTests
{
    private static readonly AssemblyIdentity Assembly = new("liveness");

    // A local read later is live; one written before any read, or never read again, is not.
    [Fact]
    public void ALocalIsLiveUntilItsLastReadAndNotAcrossAWrite()
    {
        var liveness = Liveness(Body(2,
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(4, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(6, CilOperation.Pop),
            I(7, CilOperation.Return),
        ]), 0);

        Assert.True(liveness.LocalLiveAfter(0, 0));
        Assert.False(liveness.LocalLiveAfter(1, 0));
        Assert.False(liveness.LocalLiveAfter(0, 1));
        Assert.True(liveness.LocalLiveAfter(4, 1));
    }

    // A branch back keeps what the loop reads live around it; a switch reaches each of its targets.
    [Fact]
    public void BranchesAndSwitchesCarryLiveness()
    {
        var liveness = Liveness(Body(1,
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(4, CilOperation.Switch, new CilOperand.SwitchTargets([0, 6])),
            I(5, CilOperation.Branch, new CilOperand.BranchTarget(0)),
            I(6, CilOperation.Return),
        ]), 0);

        Assert.True(liveness.LocalLiveAfter(4, 0));
        Assert.True(liveness.LocalLiveAfter(5, 0));
        Assert.True(liveness.LocalLiveAfter(2, 0));
    }

    // An argument stored before it is read is dead before the store; an argument whose address is taken, and a local
    // whose address is taken, stay live everywhere.
    [Fact]
    public void ArgumentsFollowTheirStoresAndAddressesKeepValuesLive()
    {
        var liveness = Liveness(Body(1,
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(2, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.Pop),
            I(5, CilOperation.LoadArgumentAddress, new CilOperand.Index(1)),
            I(6, CilOperation.Pop),
            I(7, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(8, CilOperation.Pop),
            I(9, CilOperation.Return),
        ]), 2);

        Assert.False(liveness.ArgumentLiveAfter(0, 0));
        Assert.True(liveness.ArgumentLiveAfter(2, 0));
        Assert.False(liveness.ArgumentLiveAfter(3, 0));
        Assert.True(liveness.ArgumentLiveAfter(9, 1));
        Assert.True(liveness.LocalLiveAfter(9, 0));
    }

    // What a finally reads is live through its protected block, and what follows a leave is live at the finally's
    // end; what a filtered handler reads is live through the block and at the filter's end.
    [Fact]
    public void HandlersKeepWhatTheyReadLive()
    {
        var finallyBody = Body(2,
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(5)),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.EndFinally),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(6, CilOperation.Pop),
            I(7, CilOperation.Return),
        ]) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 3, null, null)],
        };
        var finallyLiveness = Liveness(finallyBody, 0);

        Assert.True(finallyLiveness.LocalLiveAfter(0, 0));
        Assert.True(finallyLiveness.LocalLiveAfter(4, 1));

        var filterBody = Body(1,
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(4, CilOperation.EndFilter),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(6, CilOperation.Pop),
            I(7, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(8, CilOperation.Return),
        ]) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Filter, 0, 2, 5, 3, null, 2)],
        };
        var filterLiveness = Liveness(filterBody, 0);

        Assert.True(filterLiveness.LocalLiveAfter(0, 0));
        Assert.True(filterLiveness.LocalLiveAfter(4, 0));
    }

    // Each filter's end continues at its own handler only, whatever other regions the method has.
    [Fact]
    public void AFilterContinuesAtItsOwnHandler()
    {
        var liveness = Liveness(Body(2,
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(14)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(4, CilOperation.EndFilter),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(6, CilOperation.Pop),
            I(7, CilOperation.Leave, new CilOperand.BranchTarget(14)),
            I(8, CilOperation.Pop),
            I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(10, CilOperation.EndFilter),
            I(11, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(12, CilOperation.Pop),
            I(13, CilOperation.Leave, new CilOperand.BranchTarget(14)),
            I(14, CilOperation.Return),
        ]) with
        {
            ExceptionRegions =
            [
                new(CilExceptionRegionKind.Finally, 0, 2, 14, 0, null, null),
                new(CilExceptionRegionKind.Filter, 0, 2, 5, 3, null, 2),
                new(CilExceptionRegionKind.Filter, 0, 2, 11, 3, null, 8),
            ],
        }, 0);

        Assert.True(liveness.LocalLiveAfter(4, 0));
        Assert.False(liveness.LocalLiveAfter(4, 1));
        Assert.True(liveness.LocalLiveAfter(10, 1));
        Assert.False(liveness.LocalLiveAfter(10, 0));
    }

    private static CilLiveness Liveness(CilMethodBody body, int arguments) =>
        CilLiveness.Compute(body, arguments, CilAddressUses.Compute(body, arguments, _ => false));

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(int locals, ImmutableArray<CilInstruction> instructions)
    {
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "M", true,
            MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void)), 0);
        return new CilMethodBody(definition, 2, [.. Enumerable.Repeat(CliValueKind.I4, locals)], instructions);
    }
}
