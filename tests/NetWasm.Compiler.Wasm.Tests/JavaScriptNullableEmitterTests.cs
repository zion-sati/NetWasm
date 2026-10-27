using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class JavaScriptNullableEmitterTests
{
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    private static readonly CliTypeIdentity DateTime = CliTypeIdentity.Named(
        EmitterTestSupport.Assembly, "System", "DateTime", isValueType: true);

    private static readonly CliTypeIdentity NullableDateTime = CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(EmitterTestSupport.Assembly, "System", "Nullable`1", true), [DateTime]);

    // An import's nullable date result lies in its record as Unix milliseconds,
    // which the date conversion turns back into the struct's DateTime.
    [Fact]
    public void ReadsANullableDateFromItsRecordThroughTheDateConversion()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        Emitter().Emit(code, new JavaScriptNullableFromRecord(NullableDateTime, new(1), new(2)), Interop());

        var instructions = code.ToInstructions();
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.F64Load);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call && instruction.Operand.UnsignedValue == 12U);
    }

    [Fact]
    public void RejectsATypeThatIsNotANullableScalarOrDate() =>
        Assert.Throws<InvalidOperationException>(() => Emitter().Emit(
            new EmitterTestSupport.RecordingInstructionWriter(), new JavaScriptNullableToPair(Int32, new(1)), Interop()));

    [Fact]
    public void RejectsAMoveItDoesNotKnow() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Emitter().Emit(
            new EmitterTestSupport.RecordingInstructionWriter(), new UnknownMove(NullableDateTime, new(1)), Interop()));

    private static JavaScriptNullableEmitter Emitter()
    {
        var layouts = new RecordingLayoutProvider();
        return new(layouts, new DateLayouts(), new AddressInstructionEmitter(layouts));
    }

    private static InteropImportPlan Interop() =>
        new([], default, default, default, default, default, default)
        {
            Conversions = new Dictionary<string, int> { ["ToDateTime"] = 12 }.ToImmutableDictionary(),
        };

    private sealed record UnknownMove(CliTypeIdentity Type, WasmLocalAddress Struct) : JavaScriptNullableMove(Type, Struct);

    // DateTime is one 8-byte word.
    private sealed class DateLayouts : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) => new(type, 8, 8, []);
    }
}
