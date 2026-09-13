using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NullableBoxEmitterTests
{
    [Fact]
    public void EmitBoxesTheScalarPayloadOnlyWhenItHasAValue()
    {
        var request = Request();

        Emit(request, CliTypeIdentity.Primitive("i4", CliValueKind.I4));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load8Unsigned, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.If, GetCodeBytes(request));
    }

    [Fact]
    public void EmitCopiesAValueTypePayloadIntoItsUnderlyingBox()
    {
        var request = Request();

        Emit(request, CliTypeIdentity.Named(Assembly, "Test", "Value", true));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.MemoryCopy, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant, 4L)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant, 8L)]
    public void EmitUsesTheTargetObjectHeaderForItsPayload(
        WasmTarget target,
        byte constantOpcode,
        long expectedOffset)
    {
        var request = Request();

        Emit(request, CliTypeIdentity.Primitive("i4", CliValueKind.I4), target);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request))
            .ToInstructions();
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == constantOpcode &&
            (target == WasmTarget.Wasm64
                ? instruction.Operand.Signed64Value
                : instruction.Operand.SignedValue) == expectedOffset);
    }

    private static void Emit(
        InstructionEmissionRequest request,
        CliTypeIdentity underlyingType,
        WasmTarget target = WasmTarget.Wasm32)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var addresses = new AddressInstructionEmitter(layouts);
        INullableBoxEmitter emitter = new[]
        {
            new NullableBoxEmitter(
                layouts,
                addresses,
                layouts,
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(layouts, layouts, 7)),
        }.Cast<INullableBoxEmitter>().Single();
        emitter.Emit(request, GetCodeWriter(request), underlyingType);
    }

    private static InstructionEmissionRequest Request() =>
        CreateInstructionRequest(
            CilOperation.Box,
            [CliValueKind.ValueType],
            new CilOperand.TypeIdentity(CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true),
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])),
            CreateMethodEmissionContext() with
            {
                ValueLayout = new ValueFrameLayout(
                    16,
                    [],
                    [],
                    ImmutableDictionary<int, int>.Empty.Add(0, 4),
                    []),
            });
}
