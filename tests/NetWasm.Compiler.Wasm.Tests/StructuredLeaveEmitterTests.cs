using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StructuredLeaveEmitterTests
{
    [Fact]
    public void SelectsTheMatchingContinuationAfterSkippingEarlierTargets()
    {
        var group = Group();
        var context = Context(group);
        var instruction = new CilInstruction(
            3, 4, CilOperation.Leave, new CilOperand.BranchTarget(42));
        var code = new RecordingInstructionWriter();

        ThroughContract().Emit(
            code,
            instruction,
            new StructuredContinuationId(1),
            context);

        Assert.Equal(
            [WasmOpcodes.I32Constant, 2, WasmOpcodes.LocalSet, 29],
            code.ToArray());
    }

    [Fact]
    public void RejectsLeaveTargetsWithoutAStructuredContinuation()
    {
        var group = Group();
        var instruction = new CilInstruction(
            3, 4, CilOperation.Leave, new CilOperand.BranchTarget(42));

        Assert.Throws<InvalidOperationException>(() => ThroughContract().Emit(
            new RecordingInstructionWriter(),
            instruction,
            null,
            Context(group)));
    }

    [Fact]
    public void RejectsLeaveWithoutAnActiveExceptionGroup()
    {
        var instruction = new CilInstruction(
            3, 4, CilOperation.Leave, new CilOperand.BranchTarget(42));

        Assert.Throws<InvalidOperationException>(() => ThroughContract().Emit(
            new RecordingInstructionWriter(),
            instruction,
            null,
            CreateMethodEmissionContext()));
    }

    private static StructuredExceptionGroup Group() => new(
        new StructuredExceptionGroupId(0),
        null,
        0,
        0,
        [],
        [],
        [],
        null,
        null)
    {
        ProtectedBlocks = [],
    };

    private static MethodEmissionContext Context(StructuredExceptionGroup group) =>
        CreateMethodEmissionContext() with
        {
            ActiveExceptionGroup = group,
            ExceptionContinuationLocals =
                ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(group.Id, 29),
        };

    private static IStructuredLeaveEmitter ThroughContract() => new[]
    {
        new StructuredLeaveEmitter(),
    }.Cast<IStructuredLeaveEmitter>().Single();
}
