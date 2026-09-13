using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NullableUnboxAnyEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmitInitializesAndConditionallyPopulatesTheNullableValue(
        WasmTarget target)
    {
        var nullableType = NullableType();
        var underlyingType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var request = Request(target, nullableType);

        Emit(request, target, nullableType, underlyingType);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.MemoryFill, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.MemoryCopy, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Store8, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
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
        CliTypeIdentity underlyingType)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        INullableUnboxAnyEmitter emitter = new[]
        {
            new NullableUnboxAnyEmitter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts,
                layouts,
                new ImplicitExceptionEmitter(layouts, layouts, 7)),
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
