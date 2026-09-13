using NetWasm.Compiler.Core;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class UnsafeIntrinsicEmitterTests
{
    [Theory]
    [InlineData(RuntimeIntrinsic.UnsafeAdd, CliValueKind.ManagedAddress, CliValueKind.I4)]
    [InlineData(RuntimeIntrinsic.UnsafeAs, CliValueKind.ManagedAddress, CliValueKind.ManagedAddress)]
    [InlineData(RuntimeIntrinsic.UnsafeAsRef, CliValueKind.NativeInt, CliValueKind.NativeInt)]
    [InlineData(RuntimeIntrinsic.UnsafeAsRefManaged, CliValueKind.ManagedAddress, CliValueKind.ManagedAddress)]
    [InlineData(RuntimeIntrinsic.UnsafeNullRef, CliValueKind.ManagedAddress, CliValueKind.ManagedAddress)]
    [InlineData(RuntimeIntrinsic.UnsafeSizeOf, CliValueKind.I4, CliValueKind.I4)]
    [InlineData(RuntimeIntrinsic.UnsafeByteOffset, CliValueKind.ManagedAddress, CliValueKind.ManagedAddress)]
    [InlineData(RuntimeIntrinsic.UnsafeAddByteOffset, CliValueKind.ManagedAddress, CliValueKind.NativeInt)]
    [InlineData(RuntimeIntrinsic.UnsafeObjectAs, CliValueKind.ManagedReference, CliValueKind.ManagedReference)]
    [InlineData(
        RuntimeIntrinsic.UnsafeIsAddressGreaterThan,
        CliValueKind.ManagedAddress,
        CliValueKind.ManagedAddress)]
    public void EmitsEachUnsafeIntrinsicThroughItsCapability(
        RuntimeIntrinsic intrinsic,
        CliValueKind first,
        CliValueKind second)
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(intrinsic, layouts);
        IEnumerable<CliValueKind> stack = intrinsic switch
        {
            RuntimeIntrinsic.UnsafeNullRef or RuntimeIntrinsic.UnsafeSizeOf => [],
            RuntimeIntrinsic.UnsafeObjectAs => [first],
            _ => [first, second],
        };
        ImmutableArray<CliTypeIdentity> methodArguments = intrinsic is
            RuntimeIntrinsic.UnsafeAdd or RuntimeIntrinsic.UnsafeSizeOf
            ? [CliTypeIdentity.FromStackKind(CliValueKind.I4)]
            : [];
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic,
            stack,
            methodArguments);

        EmitThroughCapability(emitter, request);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void AddRejectsAnOpenMethodShape()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new UnsafeAddIntrinsicEmitter(layouts, layouts);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeAdd,
            [CliValueKind.ManagedAddress, CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => EmitThroughCapability(emitter, request));
    }

    [Fact]
    public void SizeOfRejectsAnOpenMethodShape()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new UnsafeSizeOfIntrinsicEmitter(layouts);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeSizeOf,
            []);

        Assert.Throws<InvalidOperationException>(() =>
            EmitThroughCapability(emitter, request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Multiply, WasmOpcodes.I32Add)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Multiply, WasmOpcodes.I64Add)]
    public void AddUsesTheTargetAddressWidth(
        WasmTarget target,
        byte multiplyOpcode,
        byte addOpcode)
    {
        var layout = WasmTargetLayout.For(target);
        var layouts = new RecordingLayoutProvider(layout);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeAdd,
            [CliValueKind.ManagedAddress, CliValueKind.I4],
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]) with
        {
            Target = layout,
        };

        EmitThroughCapability(
            new UnsafeAddIntrinsicEmitter(layouts, layouts),
            request);

        var bytes = EmitterTestSupport.GetCodeBytes(request.Instruction);
        Assert.Contains(multiplyOpcode, bytes);
        Assert.Contains(addOpcode, bytes);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant, 4)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant, 8)]
    public void AddUsesReferenceStorageSize(
        WasmTarget target,
        byte constantOpcode,
        byte expectedSize)
    {
        var layout = WasmTargetLayout.For(target);
        var layouts = new RecordingLayoutProvider(layout);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeAdd,
            [CliValueKind.ManagedAddress, CliValueKind.I4],
            [CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)]) with
        {
            Target = layout,
        };

        EmitThroughCapability(
            new UnsafeAddIntrinsicEmitter(layouts, layouts),
            request);

        Assert.True(EmitterTestSupport.GetCodeBytes(request.Instruction)
            .AsSpan().IndexOf([constantOpcode, expectedSize]) >= 0);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 1)]
    [InlineData(WasmTarget.Wasm32, 2)]
    [InlineData(WasmTarget.Wasm32, 4)]
    [InlineData(WasmTarget.Wasm32, 8)]
    [InlineData(WasmTarget.Wasm32, 16)]
    [InlineData(WasmTarget.Wasm64, 1)]
    [InlineData(WasmTarget.Wasm64, 2)]
    [InlineData(WasmTarget.Wasm64, 4)]
    [InlineData(WasmTarget.Wasm64, 8)]
    [InlineData(WasmTarget.Wasm64, 16)]
    public void AddUsesExactClosedValueTypeLayoutSize(
        WasmTarget target,
        byte expectedSize)
    {
        var layout = WasmTargetLayout.For(target);
        var layouts = new RecordingLayoutProvider(layout);
        var type = CliTypeIdentity.Primitive(
            $"value-{expectedSize}",
            CliValueKind.I4);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeAdd,
            [CliValueKind.ManagedAddress, CliValueKind.I4],
            [type]) with
        {
            Target = layout,
        };

        EmitThroughCapability(
            new UnsafeAddIntrinsicEmitter(
                layouts,
                new FixedValueLayoutProvider(expectedSize)),
            request);

        var constantOpcode = target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Constant
            : WasmOpcodes.I32Constant;
        Assert.True(EmitterTestSupport.GetCodeBytes(request.Instruction)
            .AsSpan().IndexOf([constantOpcode, expectedSize]) >= 0);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32GreaterThanUnsigned)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64GreaterThanUnsigned)]
    public void AddressComparisonUsesTheTargetAddressWidth(
        WasmTarget target,
        byte expectedOpcode)
    {
        var layout = WasmTargetLayout.For(target);
        var layouts = new RecordingLayoutProvider(layout);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeIsAddressGreaterThan,
            [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress]) with
        {
            Target = layout,
        };

        EmitThroughCapability(
            new UnsafeIsAddressGreaterThanIntrinsicEmitter(layouts),
            request);

        Assert.Contains(expectedOpcode, EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Subtract)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Subtract)]
    public void ByteOffsetUsesTheTargetAddressWidth(
        WasmTarget target,
        byte expectedOpcode)
    {
        var layout = WasmTargetLayout.For(target);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeByteOffset,
            [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress]) with
        {
            Target = layout,
        };

        EmitThroughCapability(
            new UnsafeByteOffsetIntrinsicEmitter(new RecordingLayoutProvider(layout)),
            request);

        Assert.Contains(expectedOpcode, EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Add)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Add)]
    public void AddByteOffsetUsesTheTargetAddressWidth(
        WasmTarget target,
        byte expectedOpcode)
    {
        var layout = WasmTargetLayout.For(target);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeAddByteOffset,
            [CliValueKind.ManagedAddress, CliValueKind.NativeInt]) with
        {
            Target = layout,
        };

        EmitThroughCapability(
            new UnsafeAddByteOffsetIntrinsicEmitter(new RecordingLayoutProvider(layout)),
            request);

        Assert.Contains(expectedOpcode, EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void UnboxDelegatesTheClosedValueTypeToTheUnboxStrategy()
    {
        var unboxes = new RecordingUnboxEmitter();
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeUnbox,
            [CliValueKind.ManagedReference],
            [type]);

        EmitThroughCapability(new UnsafeUnboxIntrinsicEmitter(unboxes), request);

        Assert.Same(request.Instruction, unboxes.Request);
        Assert.Equal(type, unboxes.Type);
        Assert.False(unboxes.CopyValue);
    }

    [Fact]
    public void UnboxRejectsAnOpenMethodShape()
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.UnsafeUnbox,
            [CliValueKind.ManagedReference]);

        Assert.Throws<InvalidOperationException>(() => EmitThroughCapability(
            new UnsafeUnboxIntrinsicEmitter(new RecordingUnboxEmitter()),
            request));
    }

    private static IRuntimeIntrinsicEmitter CreateEmitter(
        RuntimeIntrinsic intrinsic,
        RecordingLayoutProvider layouts) =>
        intrinsic switch
        {
            RuntimeIntrinsic.UnsafeAdd => new UnsafeAddIntrinsicEmitter(layouts, layouts),
            RuntimeIntrinsic.UnsafeAs => new UnsafeAsIntrinsicEmitter(),
            RuntimeIntrinsic.UnsafeAsRef => new UnsafeAsRefIntrinsicEmitter(),
            RuntimeIntrinsic.UnsafeAsRefManaged => new UnsafeAsRefManagedIntrinsicEmitter(),
            RuntimeIntrinsic.UnsafeNullRef => new UnsafeNullRefIntrinsicEmitter(
                EmitterTestSupport.CreateAddressInstructions(layouts)),
            RuntimeIntrinsic.UnsafeIsAddressGreaterThan =>
                new UnsafeIsAddressGreaterThanIntrinsicEmitter(layouts),
            RuntimeIntrinsic.UnsafeSizeOf => new UnsafeSizeOfIntrinsicEmitter(layouts),
            RuntimeIntrinsic.UnsafeByteOffset => new UnsafeByteOffsetIntrinsicEmitter(layouts),
            RuntimeIntrinsic.UnsafeAddByteOffset =>
                new UnsafeAddByteOffsetIntrinsicEmitter(layouts),
            RuntimeIntrinsic.UnsafeObjectAs => new UnsafeObjectAsIntrinsicEmitter(),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };

    private sealed class FixedValueLayoutProvider(int size) : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            new(type, size, Math.Min(size, 8), []);
    }

    private static void EmitThroughCapability(
        IRuntimeIntrinsicEmitter emitter,
        RuntimeIntrinsicEmissionRequest request) => emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));

    private sealed class RecordingUnboxEmitter : IUnboxEmitter
    {
        public InstructionEmissionRequest? Request { get; private set; }
        public CliTypeIdentity? Type { get; private set; }
        public bool? CopyValue { get; private set; }

        public void Unbox(
            InstructionEmissionRequest request,
            IWasmInstructionWriter code,
            bool copyValue) => throw new NotSupportedException();

        public void Unbox(
            InstructionEmissionRequest request,
            IWasmInstructionWriter code,
            CliTypeIdentity type,
            bool copyValue)
        {
            Request = request;
            Type = type;
            CopyValue = copyValue;
        }
    }
}
