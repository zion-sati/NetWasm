using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

// A boundary entered from JavaScript holds its dates and nullables as structs in one
// value frame: each such parameter's slot in parameter order, then the result's.
public sealed class JavaScriptValueFrameEmitterTests
{
    private static readonly CliTypeIdentity DateTime = CliTypeIdentity.Named(
        EmitterTestSupport.Assembly, "System", "DateTime", isValueType: true);

    private static readonly CliTypeIdentity DateTimeOffset = CliTypeIdentity.Named(
        EmitterTestSupport.Assembly, "System", "DateTimeOffset", isValueType: true);

    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    private static readonly CliTypeIdentity NullableInt32 = CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(EmitterTestSupport.Assembly, "System", "Nullable`1", true), [Int32]);

    private static readonly ImmutableDictionary<string, int> Conversions = new Dictionary<string, int>
    {
        ["FromDateTime"] = 11,
        ["ToDateTime"] = 12,
        ["FromDateTimeOffset"] = 13,
        ["ToDateTimeOffset"] = 14,
    }.ToImmutableDictionary();

    [Fact]
    public void PlansSlotsInParameterOrderThenTheResult()
    {
        var frame = Planner().Plan([DateTimeOffset, Int32, DateTime], DateTime);

        Assert.NotNull(frame);
        Assert.Equal([(0, 0), (2, 16)], frame.Parameters.Select(slot => (slot.Key, slot.Value.Offset)));
        Assert.Equal(24, frame.Result?.Offset);
        Assert.Equal(32, frame.Size);
    }

    // The host reads a nullable result's record after the call returns.
    [Fact]
    public void NullableResultHasARecordAfterItsStruct()
    {
        var frame = Planner().Plan([NullableInt32], NullableInt32);

        Assert.NotNull(frame);
        Assert.Equal(0, frame.Parameters[0].Offset);
        Assert.Equal(8, frame.Result?.Offset);
        Assert.Equal(16, frame.Result?.RecordOffset);
        Assert.Equal(16 + JavaScriptNullable.RecordSize, frame.Size);
    }

    [Fact]
    public void BoundaryWithoutConvertedValuesHasNoFrame() =>
        Assert.Null(Planner().Plan([Int32], Int32));

    [Fact]
    public void DateWithoutReachableConversionsIsAnInvariantFailure()
    {
        var emitter = Emitter();
        var frame = Planner().Plan([DateTime], Int32)!;

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            new EmitterTestSupport.RecordingInstructionWriter(),
            new JavaScriptValueFrameEnter(frame, 9, [0], Interop(ImmutableDictionary<string, int>.Empty))));
    }

    [Fact]
    public void EnteringConvertsEachDateParameterIntoItsSlot()
    {
        var emitter = Emitter();
        var frame = Planner().Plan([DateTimeOffset, Int32, DateTime], Int32)!;
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(code, new JavaScriptValueFrameEnter(frame, 9, [100, 101, 102], Interop(Conversions)));

        var enter = (uint)WasmRuntimeImports.CreateCatalog().Resolve(RuntimeImportSymbol.ValueFrameEnter);
        Assert.Equal(
            [
                (WasmOpcodes.Call, enter),
                (WasmOpcodes.LocalGet, 9U), (WasmOpcodes.LocalGet, 100U), (WasmOpcodes.Call, 14U),
                (WasmOpcodes.LocalGet, 9U), (WasmOpcodes.LocalGet, 102U), (WasmOpcodes.Call, 12U),
            ],
            CallsAndLocals(code));
    }

    // A nullable parameter arrives as its presence and its value, in two locals.
    [Fact]
    public void EnteringBuildsANullableFromItsPair()
    {
        var emitter = Emitter();
        var frame = Planner().Plan([Int32, NullableInt32], Int32)!;
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(
            code, new JavaScriptValueFrameEnter(frame, 9, [100, 101], Interop(ImmutableDictionary<string, int>.Empty)));

        var locals = CallsAndLocals(code).Where(item => item.Opcode == WasmOpcodes.LocalGet)
            .Select(item => item.Operand).ToArray();
        Assert.Equal([9U, 101U, 9U, 9U, 102U], locals);
        Assert.Contains(code.ToInstructions(), instruction => instruction.Opcode == WasmOpcodes.I32Store8);
    }

    [Fact]
    public void ResultLeavesItsUnixMilliseconds()
    {
        var emitter = Emitter();
        var frame = Planner().Plan([], DateTimeOffset)!;
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(code, new JavaScriptValueFrameResult(frame, 9, Interop(Conversions)));

        var instructions = code.ToInstructions();
        Assert.Equal((WasmOpcodes.LocalGet, 9U), (instructions[0].Opcode, instructions[0].Operand.UnsignedValue));
        Assert.Equal((WasmOpcodes.Call, 13U), (instructions[^1].Opcode, instructions[^1].Operand.UnsignedValue));
    }

    [Fact]
    public void NullableResultLeavesItsRecordAddress()
    {
        var emitter = Emitter();
        var frame = Planner().Plan([], NullableInt32)!;
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(code, new JavaScriptValueFrameResult(frame, 9, Interop(ImmutableDictionary<string, int>.Empty)));

        var instructions = code.ToInstructions();
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Store);
        Assert.Equal((WasmOpcodes.LocalGet, 9U), (instructions[^3].Opcode, instructions[^3].Operand.UnsignedValue));
        Assert.Equal(WasmOpcodes.I32Add, instructions[^1].Opcode);
    }

    [Fact]
    public void AddressAndLeaveUseTheFrameLocal()
    {
        var emitter = Emitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(code, new JavaScriptValueFrameAddress(9, 0));
        emitter.Emit(code, new JavaScriptValueFrameAddress(9, 16));
        emitter.Emit(code, new JavaScriptValueFrameLeave(9));

        var leave = (uint)WasmRuntimeImports.CreateCatalog().Resolve(RuntimeImportSymbol.ValueFrameLeave);
        Assert.Equal(
            [(WasmOpcodes.LocalGet, 9U), (WasmOpcodes.LocalGet, 9U), (WasmOpcodes.LocalGet, 9U), (WasmOpcodes.Call, leave)],
            CallsAndLocals(code));
        Assert.Equal(WasmOpcodes.I32Add, code.ToInstructions()[3].Opcode);
    }

    [Fact]
    public void ResultOfAFrameWithoutAConvertedResultIsAnInvariantFailure()
    {
        var frame = Planner().Plan([DateTime], Int32)!;

        Assert.Throws<InvalidOperationException>(() => Emitter().Emit(
            new EmitterTestSupport.RecordingInstructionWriter(),
            new JavaScriptValueFrameResult(frame, 9, Interop(Conversions))));
    }

    [Fact]
    public void RejectsAStepItDoesNotKnow() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Emitter().Emit(
            new EmitterTestSupport.RecordingInstructionWriter(), new UnknownStep(9)));

    private sealed record UnknownStep(int FrameLocal) : JavaScriptValueFrameStep(FrameLocal);

    private static (byte Opcode, uint Operand)[] CallsAndLocals(EmitterTestSupport.RecordingInstructionWriter code) =>
        [.. code.ToInstructions()
            .Where(instruction => instruction.Opcode is WasmOpcodes.Call or WasmOpcodes.LocalGet)
            .Select(instruction => (instruction.Opcode, instruction.Operand.UnsignedValue))];

    private static JavaScriptValueFramePlanner Planner() => new(new BoundaryLayouts());

    private static JavaScriptValueFrameEmitter Emitter()
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new AddressInstructionEmitter(layouts);
        return new(layouts, WasmRuntimeImports.CreateCatalog(), addresses,
            new JavaScriptNullableEmitter(layouts, new BoundaryLayouts(), addresses));
    }

    private static InteropImportPlan Interop(ImmutableDictionary<string, int> conversions) =>
        new([], default, default, default, default, default, default) { Conversions = conversions };

    // DateTime is one 8-byte word; DateTimeOffset adds its offset in minutes;
    // Nullable<int> holds its presence in its first byte and the int at 4.
    private sealed class BoundaryLayouts : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) => type switch
        {
            _ when type.FullName == "System.DateTime" => new(type, 8, 8, []),
            _ when type.FullName == "System.DateTimeOffset" => new(type, 16, 8, []),
            _ when type == NullableInt32 => new(type, 8, 4, []),
            _ when type == Int32 => new(type, 4, 4, []),
            _ => throw new InvalidOperationException($"'{type.CanonicalName}' has no test layout"),
        };
    }
}
