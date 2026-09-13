using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class BoxingInstructionEmitterTests
{
    [Fact]
    public void BoxAllocatesAndProducesManagedReference()
    {
        var safepoints = 0;
        var emitter = AsProvider(CreateEmitter(_ => safepoints++));
        var request = CreateRequest(
            CilOperation.Box,
            CliValueKind.I4,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));

        Emit(emitter, request);

        Assert.Equal(1, safepoints);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant, 4L)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant, 8L)]
    public void BoxUsesTheTargetObjectHeaderForItsPayload(
        WasmTarget target,
        byte constantOpcode,
        long expectedOffset)
    {
        var request = CreateRequest(
            CilOperation.Box,
            CliValueKind.I4,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));

        Emit(AsProvider(CreateEmitter(target)), request);

        AssertPayloadOffset(request, target, constantOpcode, expectedOffset);
    }

    [Fact]
    public void UnboxProducesManagedAddressAndChecksType()
    {
        var request = CreateRequest(
            CilOperation.Unbox,
            CliValueKind.ManagedReference,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));

        Emit(AsProvider(CreateEmitter()), request);

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant, 4L)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant, 8L)]
    public void UnboxUsesTheTargetObjectHeaderForItsPayload(
        WasmTarget target,
        byte constantOpcode,
        long expectedOffset)
    {
        var request = CreateRequest(
            CilOperation.Unbox,
            CliValueKind.ManagedReference,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));

        CreateUnboxEmitter(target).Unbox(
            request,
            GetCodeWriter(request),
            copyValue: false);

        AssertPayloadOffset(request, target, constantOpcode, expectedOffset);
    }

    [Fact]
    public void BoxValueTypeCopiesItsPayloadIntoTheAllocatedObject()
    {
        var request = CreateRequest(
            CilOperation.Box,
            CliValueKind.ValueType,
            ValueType());

        Emit(AsProvider(CreateEmitter()), request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.MemoryCopy, GetCodeBytes(request));
    }

    [Fact]
    public void BoxNullableDelegatesToNullableBoxingSemantics()
    {
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var request = CreateRequest(
            CilOperation.Box,
            CliValueKind.ValueType,
            nullable);

        Emit(AsProvider(CreateEmitter()), request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void BoxReferenceReturnsTheExistingManagedReference()
    {
        var safepoints = 0;
        var request = CreateRequest(
            CilOperation.Box,
            CliValueKind.ManagedReference,
            ReferenceType());

        Emit(AsProvider(CreateEmitter(_ => safepoints++)), request);

        Assert.Equal(1, safepoints);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Empty(GetCodeBytes(request));
    }

    [Fact]
    public void BoxReferenceRejectsAValueStackRepresentation()
    {
        var request = CreateRequest(
            CilOperation.Box,
            CliValueKind.I4,
            ReferenceType());

        var exception = Assert.Throws<CompilerException>(() =>
            Emit(AsProvider(CreateEmitter()), request));

        Assert.Contains("requires a managed reference", exception.Message);
    }

    [Fact]
    public void UnboxCopiesScalarPayloadIntoItsTypedStackLocal()
    {
        var request = CreateRequest(
            CilOperation.Unbox,
            CliValueKind.ManagedReference,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));

        CreateUnboxEmitter().Unbox(
            request,
            GetCodeWriter(request),
            copyValue: true);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant)]
    public void UnboxCopiesValueTypePayloadThroughItsReservedValueFrameTemporary(
        WasmTarget target,
        byte constantOpcode)
    {
        var request = CreateRequest(
            CilOperation.Unbox,
            CliValueKind.ManagedReference,
            ValueType(),
            CreateValueCopyContext());

        CreateUnboxEmitter(target).Unbox(
            request,
            GetCodeWriter(request),
            copyValue: true);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.MemoryCopy, GetCodeBytes(request));
        AssertPayloadOffset(request, target, constantOpcode, 12);
    }

    private static BoxingInstructionEmitter CreateEmitter(
        Action<InstructionEmissionRequest>? safepoint = null)
        => CreateEmitter(WasmTarget.Wasm32, safepoint);

    private static BoxingInstructionEmitter CreateEmitter(
        WasmTarget target,
        Action<InstructionEmissionRequest>? safepoint = null)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var addresses = new AddressInstructionEmitter(layouts);
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var exceptions = new ImplicitExceptionEmitter(layouts, layouts, 7);
        var types = CreateTypeOperands(new FakeProgram());
        var unboxes = new BoxedValueUnboxEmitter(
            layouts,
            addresses,
            layouts,
            types,
            exceptions,
            new BoxedValueTypeValidator(
                layouts,
                new EmptyEnumStorageResolver(),
                exceptions),
            new ValueFrameAddressEmitter(layouts));
        return new BoxingInstructionEmitter(layouts, addresses, layouts, layouts, types,
            runtimeImports,
            exceptions,
            new RecordingRootPublicationEmitter(safepoint ?? (_ => { })),
            new NullableTypeResolver(),
            new NullableBoxEmitter(
                layouts,
                addresses,
                layouts,
                layouts,
                runtimeImports,
                exceptions),
            unboxes);
    }

    private static IInstructionCommandProvider AsProvider(
        BoxingInstructionEmitter emitter) =>
        new[] { emitter }.Cast<IInstructionCommandProvider>().Single();

    private static BoxedValueUnboxEmitter CreateUnboxEmitter()
        => CreateUnboxEmitter(WasmTarget.Wasm32);

    private static BoxedValueUnboxEmitter CreateUnboxEmitter(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        return new BoxedValueUnboxEmitter(
            layouts,
            new AddressInstructionEmitter(layouts),
            layouts,
            CreateTypeOperands(new FakeProgram()),
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            new BoxedValueTypeValidator(
                layouts,
                new EmptyEnumStorageResolver(),
                new ImplicitExceptionEmitter(layouts, layouts, 7)),
            new ValueFrameAddressEmitter(layouts));
    }

    private static void Emit(
        IInstructionCommandProvider provider,
        InstructionEmissionRequest request)
    {
        var command = provider.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(
            request,
            GetCodeWriter(request),
            CreateFunctionIndexResolver());
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        CliValueKind stackType,
        CliTypeIdentity type,
        MethodEmissionContext? context = null) => CreateInstructionRequest(
            operation,
            [stackType],
            new CilOperand.TypeIdentity(type),
            context);

    private static CliTypeIdentity ValueType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Value", isValueType: true);

    private static CliTypeIdentity ReferenceType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Reference", isValueType: false);

    private static void AssertPayloadOffset(
        InstructionEmissionRequest request,
        WasmTarget target,
        byte constantOpcode,
        long expectedOffset)
    {
        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request))
            .ToInstructions();
        var addOpcode = target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Add
            : WasmOpcodes.I32Add;
        Assert.Contains(instructions.Zip(instructions.Skip(1)), pair =>
            pair.First.Opcode == constantOpcode &&
            (target == WasmTarget.Wasm64
                ? pair.First.Operand.Signed64Value
                : pair.First.Operand.SignedValue) == expectedOffset &&
            pair.Second.Opcode == addOpcode);
    }

    private static MethodEmissionContext CreateValueCopyContext() =>
        CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                32,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 12),
                []),
        };
}
