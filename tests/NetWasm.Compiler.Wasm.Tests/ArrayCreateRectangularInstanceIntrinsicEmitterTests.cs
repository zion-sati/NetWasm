using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayCreateRectangularInstanceIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SelectsRectangularTypesByElementAndRankThroughBothAllocators(WasmTarget target)
    {
        var layout = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
        var descriptors = new Descriptors([
            new(integer, 1, 0, 16, [], null),
            new(CliTypeIdentity.SzArray(integer), 76, 0, 16, [], null),
            new(CliTypeIdentity.Array(CliTypeIdentity.GenericParameter(false, 0), 2), 66, 0, 16, [], null),
            new(CliTypeIdentity.Array(integer, 2), 77, 0, 16, [], null),
            new(CliTypeIdentity.Array(text, 3), 88, 0, 16, [], null)]);
        var roots = new RecordingRoots();
        var addresses = new RecordingAddresses();
        var exceptions = new RecordingExceptions();
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ArrayCreateRectangularInstance,
            [CliValueKind.I4, CliValueKind.I4, CliValueKind.NativeInt, CliValueKind.I4]) with
        {
            Target = WasmTargetLayout.For(target),
        };
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(new ArrayCreateRectangularInstanceIntrinsicEmitter(
            descriptors, layout, layout, imports, roots, addresses, exceptions));
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var code = writer.ToInstructions();
        Assert.Same(request.Instruction, roots.Request);
        Assert.Equal([0], addresses.Constants);
        Assert.Equal([AddressOperation.EqualZero, AddressOperation.EqualZero], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.OutOfMemory], exceptions.Kinds);
        var calls = code.Where(i => i.Opcode == WasmOpcodes.Call).Select(i => i.Operand.UnsignedValue).ToArray();
        var bounded = (uint)imports.Resolve(RuntimeImportSymbol.AllocateBoundedRectangularArray, request.Instruction.Target.RuntimeImportSelection);
        var plain = (uint)imports.Resolve(RuntimeImportSymbol.AllocateRectangularArray, request.Instruction.Target.RuntimeImportSelection);
        Assert.Equal([bounded, plain, bounded, plain], calls);
        Assert.Equal(2, code.Count(i => i.Opcode == WasmOpcodes.Else));
        Assert.Contains(code, i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue == 77);
        Assert.Contains(code, i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue == 88);
        Assert.DoesNotContain(code, i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue is 76 or 66);
        Assert.Contains(code, i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue == 3);
        Assert.Equal(4, code.Count(i => i.Opcode == WasmOpcodes.LocalGet &&
            i.Operand.UnsignedValue == (uint)request.Local(2, CliValueKind.NativeInt)));
        Assert.Equal(2, code.Count(i => i.Opcode == WasmOpcodes.LocalGet &&
            i.Operand.UnsignedValue == (uint)request.Local(3, CliValueKind.I4)));
    }

    private sealed class Descriptors(ImmutableArray<ConstructedTypeDescriptorLayout> constructed) : ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => constructed;
    }

    private sealed class RecordingRoots : IRootPublicationEmitter
    {
        public InstructionEmissionRequest? Request { get; private set; }

        public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, bool constructorCall = false)
        {
            Assert.False(constructorCall);
            Request = request;
        }
    }

    private sealed class RecordingAddresses : IAddressInstructionEmitter
    {
        public List<int> Constants { get; } = [];
        public List<AddressOperation> Operations { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) => Constants.Add(constant);
        public void Emit(IWasmInstructionWriter code, AddressOperation operation) => Operations.Add(operation);
    }

    private sealed class RecordingExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];
        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) => Kinds.Add(kind);
    }
}
