using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ValueObjectBlockMemoryEmitterTests
{
    [Fact]
    public void OwnsCompleteValueObjectBlockMemoryInstructionFamily()
    {
        var emitter = CreateEmitter();

        var expected = (CilOperation[])
        [
            CilOperation.InitializeObject,
            CilOperation.SizeOf,
            CilOperation.LocalAllocate,
            CilOperation.CopyBlock,
            CilOperation.InitializeBlock,
            CilOperation.DefaultValue,
            CilOperation.LoadObject,
            CilOperation.StoreObject,
            CilOperation.CopyObject,
        ];

        Assert.Equal(expected, emitter.Commands.Select(command => command.Operation));
    }

    [Fact]
    public void InitializeObjectClearsTheValueAtItsStackAddress()
    {
        var request = CreateRequest(
            CilOperation.InitializeObject,
            [CliValueKind.ManagedAddress],
            new CilOperand.TypeIdentity(ValueType()));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void InitializeAndCopyBlocksConsumeThreeOperands()
    {
        var emitter = CreateEmitter();
        var copy = CreateRequest(
            CilOperation.CopyBlock,
            [
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
            ]);
        var initialize = CreateRequest(
            CilOperation.InitializeBlock,
            [
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.I4,
            ]);

        Emit((IInstructionCommandProvider)emitter, copy);
        Emit((IInstructionCommandProvider)emitter, initialize);

        Assert.Empty(copy.Stack);
        Assert.Empty(initialize.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, EmitterTestSupport.GetCodeBytes(copy));
        Assert.Contains(WasmOpcodes.Prefixed, EmitterTestSupport.GetCodeBytes(initialize));
    }

    [Fact]
    public void LocalAllocationReturnsTargetNativeInteger()
    {
        var request = CreateRequest(
            CilOperation.LocalAllocate,
            [CliValueKind.I4]);

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void Memory64LocalAllocationPreservesNativeSizeForTheRuntimeImport()
    {
        var request = CreateRequest(
            CilOperation.LocalAllocate,
            [CliValueKind.NativeInt]);

        Emit((IInstructionCommandProvider)CreateEmitter(WasmTargetLayout.Wasm64), request);

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.DoesNotContain(WasmOpcodes.I32WrapI64, GetCodeBytes(request));
    }

    [Fact]
    public void Memory64LocalAllocationExtendsI4SizeForTheRuntimeImport()
    {
        var request = CreateRequest(
            CilOperation.LocalAllocate,
            [CliValueKind.I4]);

        Emit((IInstructionCommandProvider)CreateEmitter(WasmTargetLayout.Wasm64), request);

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.I64ExtendI32Unsigned, GetCodeBytes(request));
    }

    [Fact]
    public void ScalarDefaultUsesZeroOfRequestedKind()
    {
        var request = CreateRequest(
            CilOperation.DefaultValue,
            [],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.I8)));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Equal([CliValueKind.I8], request.Stack);
        Assert.Equal(WasmOpcodes.I32Constant, GetCodeBytes(request)[0]);
    }

    [Fact]
    public void Memory64ScalarDefaultStillUsesAnI32Constant()
    {
        var request = CreateRequest(
            CilOperation.DefaultValue,
            [],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.I8)));

        Emit((IInstructionCommandProvider)CreateEmitter(WasmTargetLayout.Wasm64), request);

        Assert.Equal([CliValueKind.I8], request.Stack);
        Assert.Equal(WasmOpcodes.I32Constant, GetCodeBytes(request)[0]);
    }

    [Fact]
    public void Wasm32ReferenceDefaultUsesAnI32Constant()
    {
        var request = CreateRequest(
            CilOperation.DefaultValue,
            [],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(WasmOpcodes.I32Constant, GetCodeBytes(request)[0]);
    }

    [Fact]
    public void Wasm32AddressDefaultUsesAnI32Constant()
    {
        var request = CreateRequest(
            CilOperation.DefaultValue,
            [],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress)));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
        Assert.Equal(WasmOpcodes.I32Constant, GetCodeBytes(request)[0]);
    }

    [Fact]
    public void Memory64BlockLengthWidensI32()
    {
        var request = CreateRequest(
            CilOperation.CopyBlock,
            [
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
            ]);

        Emit((IInstructionCommandProvider)CreateEmitter(WasmTargetLayout.Wasm64), request);

        Assert.Contains(WasmOpcodes.I64ExtendI32Unsigned, GetCodeBytes(request));
    }

    [Fact]
    public void SizeOfPushesTheResolvedValueLayoutSize()
    {
        var request = CreateRequest(
            CilOperation.SizeOf,
            [],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.I32Constant, GetCodeBytes(request));
    }

    [Fact]
    public void CopyObjectConsumesSourceAndDestinationAddresses()
    {
        var request = CreateRequest(
            CilOperation.CopyObject,
            [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void Memory64ReferenceDefaultUsesAnI64Constant()
    {
        var request = CreateRequest(
            CilOperation.DefaultValue,
            [],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)));

        Emit((IInstructionCommandProvider)CreateEmitter(WasmTargetLayout.Wasm64), request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(WasmOpcodes.I64Constant, GetCodeBytes(request)[0]);
    }

    [Fact]
    public void Memory64AddressDefaultUsesAnI64Constant()
    {
        var request = CreateRequest(
            CilOperation.DefaultValue,
            [],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress)));

        Emit((IInstructionCommandProvider)CreateEmitter(WasmTargetLayout.Wasm64), request);

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
        Assert.Equal(WasmOpcodes.I64Constant, GetCodeBytes(request)[0]);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm32, 4)]
    [InlineData(WasmTarget.Wasm64, 4)]
    public void ValueDefaultUsesItsPlannedTemporaryAddress(
        WasmTarget target,
        int offset)
    {
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, offset),
                []),
        };
        var request = CreateRequest(
            CilOperation.DefaultValue,
            [],
            new CilOperand.TypeIdentity(valueType),
            context);

        Emit((IInstructionCommandProvider)CreateEmitter(WasmTargetLayout.For(target)), request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void LoadObjectCopiesAValueThroughItsTemporaryFrameAddress()
    {
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                []),
        };
        var request = CreateRequest(
            CilOperation.LoadObject,
            [CliValueKind.ManagedAddress],
            new CilOperand.TypeIdentity(ValueType()),
            context);

        Emit((IInstructionCommandProvider)CreateEmitter(WasmTargetLayout.Wasm64), request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void LoadObjectUsesTypedMemoryLoadForScalarValues()
    {
        var request = CreateRequest(
            CilOperation.LoadObject,
            [CliValueKind.ManagedAddress],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
    }

    [Fact]
    public void StoreObjectCopiesAValueThroughManagedMemory()
    {
        var request = CreateRequest(
            CilOperation.StoreObject,
            [CliValueKind.ManagedAddress, CliValueKind.ValueType],
            new CilOperand.TypeIdentity(ValueType()));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void StoreObjectUsesTypedMemoryStoreForScalarValues()
    {
        var request = CreateRequest(
            CilOperation.StoreObject,
            [CliValueKind.ManagedAddress, CliValueKind.I4],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)));

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
    }

    private static CliTypeIdentity ValueType() => CliTypeIdentity.Named(
        Assembly,
        "Test",
        "Value",
        isValueType: true,
        CliValueKind.ValueType);

    private static void Emit<TProvider>(
        TProvider provider,
        InstructionEmissionRequest request)
        where TProvider : IInstructionCommandProvider
    {
        var command = provider.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());
    }

    private static ValueObjectBlockMemoryEmitter CreateEmitter(
        WasmTargetLayout? target = null)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(target);
        return new ValueObjectBlockMemoryEmitter(layouts, layouts, CreateTypeOperands(program),
            WasmRuntimeImports.CreateCatalog());
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        IEnumerable<CliValueKind> stack,
        CilOperand? operand = null,
        MethodEmissionContext? context = null)
    {
        return CreateInstructionRequest(
            operation,
            stack,
            operand,
            context,
            maxStack: 4);
    }
}
