using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class UnboxAnyInstructionEmitterTests
{
    [Fact]
    public void ValueTypeCopiesBoxedPayloadIntoStackRepresentation()
    {
        var request = CreateRequest(
            CliTypeIdentity.FromStackKind(CliValueKind.I4));

        CreateEmitter().Emit(request);

        Assert.Equal([CliValueKind.I4], request.Stack);
    }

    [Fact]
    public void ReferenceTypeUsesCastSemantics()
    {
        var request = CreateRequest(
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));

        CreateEmitter().Emit(request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    [Fact]
    public void NullableValueUsesNullableUnboxSemantics()
    {
        var request = CreateRequest(CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]));

        CreateEmitter().Emit(request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.MemoryFill, GetCodeBytes(request));
    }

    private static UnboxAnyInstructionEmitter CreateEmitter()
    {
        var layouts = new RecordingLayoutProvider();
        var types = CreateTypeOperands(new FakeProgram());
        var exceptions = new ImplicitExceptionEmitter(layouts, layouts, 7);
        var addresses = new AddressInstructionEmitter(layouts);
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
        var typeTests = new TypeTestInstructionEmitter(layouts, new AddressInstructionEmitter(layouts), layouts, types,
            WasmRuntimeImports.CreateCatalog(),
            exceptions,
            new NullableTypeResolver());
        return new UnboxAnyInstructionEmitter(
            types,
            unboxes,
            typeTests,
            new NullableTypeResolver(),
            new NullableUnboxAnyEmitter(
                layouts,
                addresses,
                layouts,
                layouts,
                exceptions));
    }

    private static InstructionEmissionRequest CreateRequest(CliTypeIdentity type)
    {
        return CreateInstructionRequest(
            CilOperation.UnboxAny,
            [CliValueKind.ManagedReference],
            new CilOperand.TypeIdentity(type),
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
}
