using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NullableUnboxAnyEmitterTests
{
    // The value is zeroed first: with stores up to 64 bytes, by memory.fill above.
    [Theory]
    [InlineData(WasmTarget.Wasm32, 4, false)]
    [InlineData(WasmTarget.Wasm64, 4, false)]
    [InlineData(WasmTarget.Wasm32, 80, true)]
    public void EmitInitializesAndConditionallyPopulatesTheNullableValue(
        WasmTarget target,
        int size,
        bool fills)
    {
        var nullableType = NullableType();
        var underlyingType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var request = Request(target, nullableType);

        Emit(request, target, nullableType, underlyingType, size);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Equal(fills, ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions().Any(instruction =>
            instruction.Opcode == WasmOpcodes.Prefixed &&
            instruction.Operand == WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        Assert.Contains(WasmOpcodes.MemoryCopy, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Store8, GetCodeBytes(request));
        Assert.True(ContainsManagedThrow(GetCodeBytes(request)));
        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request))
            .ToInstructions();
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == (target == WasmTarget.Wasm64
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant) &&
            (target == WasmTarget.Wasm64
                ? instruction.Operand.Signed64Value
                : instruction.Operand.SignedValue) ==
            WasmTargetLayout.For(target).ObjectHeaderSize);
    }

    private static void Emit(
        InstructionEmissionRequest request,
        WasmTarget target,
        CliTypeIdentity nullableType,
        CliTypeIdentity underlyingType,
        int size)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target), size);
        INullableUnboxAnyEmitter emitter = new[]
        {
            new NullableUnboxAnyEmitter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts,
                layouts,
                new ImplicitExceptionEmitter(layouts, layouts)),
        }.Cast<INullableUnboxAnyEmitter>().Single();
        emitter.Emit(request, GetCodeWriter(request), nullableType, underlyingType);
    }

    private static InstructionEmissionRequest Request(
        WasmTarget target,
        CliTypeIdentity nullableType) => CreateInstructionRequest(
        CilOperation.UnboxAny,
        [CliValueKind.ManagedReference],
        new CilOperand.TypeIdentity(nullableType),
        CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                []),
        });

    private static CliTypeIdentity NullableType() =>
        CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
}
