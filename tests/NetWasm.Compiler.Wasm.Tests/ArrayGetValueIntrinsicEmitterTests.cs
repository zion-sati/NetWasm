using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class ArrayGetValueIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void PublishesRootsAndTranslatesTheRuntimeAllocationFailureSentinel(WasmTarget target, bool nullable)
    {
        var layout = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var descriptors = new Descriptors(nullable);
        var receiver = new RecordingReceiver();
        var addresses = new RecordingAddresses();
        var exceptions = new RecordingExceptions();
        var roots = new RecordingRoots();
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(
            new ArrayGetValueIntrinsicEmitter(receiver, imports, addresses, exceptions, roots, descriptors, new NullableTypeResolver(), layout, layout, layout));
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ArrayGetValue,
            [CliValueKind.ManagedReference, CliValueKind.I4]) with
        { Target = WasmTargetLayout.For(target) };
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var code = writer.ToInstructions();
        Assert.Equal(request.Local(0, CliValueKind.ManagedReference), receiver.Local);
        Assert.Same(writer, receiver.Writer);
        Assert.Same(request.Instruction, roots.Request);
        Assert.Same(writer, roots.Writer);
        Assert.Equal(0, receiver.InstructionCount);
        Assert.Equal(0, roots.InstructionCount);
        var tail = code.TakeLast(8).ToArray();
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.LocalGet, WasmOpcodes.LocalGet,
            WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalTee, WasmOpcodes.If, WasmOpcodes.End], tail.Select(item => item.Opcode));
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), tail[0].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(1, CliValueKind.I4), tail[1].Operand.UnsignedValue);
        Assert.Equal((uint)request.Instruction.Context.NumericTemporaryI4Second, tail[2].Operand.UnsignedValue);
        Assert.Equal((uint)request.Instruction.Context.ObjectTemporary, tail[3].Operand.UnsignedValue);
        Assert.Equal((uint)imports.Resolve(RuntimeImportSymbol.ArrayGetValue, request.Instruction.Target.RuntimeImportSelection), tail[4].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), tail[5].Operand.UnsignedValue);
        Assert.Equal<int>(nullable ? [0, 4, 1] : [0, 1], addresses.Constants);
        Assert.Equal(nullable ? 2 : 1, code.Count(i => i.Opcode == WasmOpcodes.If));
        Assert.Equal(nullable ? Descriptors.Underlying : null, layout.ObjectIdentityRequest);
        Assert.Equal((uint)layout.ArrayElementTypeIdOffset, code[1].Operand.Offset);
        Assert.Equal([AddressOperation.Equal], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.OutOfMemory], exceptions.Kinds);
    }

    [Fact]
    public void SkipsANullableDescriptorWhoseUnderlyingTypeIsNeverBoxed()
    {
        // Analysis boxes T only for Nullable<T> array elements, so a Nullable<T> that no array
        // holds has no boxed layout and needs no element branch.
        var layout = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var addresses = new RecordingAddresses();
        var emitter = new ArrayGetValueIntrinsicEmitter(new RecordingReceiver(), WasmRuntimeImports.CreateCatalog(),
            addresses, new RecordingExceptions(), new RecordingRoots(), new Descriptors(true),
            new NullableTypeResolver(), new UnboxedLayouts(layout), layout, layout);
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ArrayGetValue,
            [CliValueKind.ManagedReference, CliValueKind.I4]);
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        Assert.Equal(1, writer.ToInstructions().Count(i => i.Opcode == WasmOpcodes.If));
        Assert.Equal<int>([0, 1], addresses.Constants);
    }

    private sealed class UnboxedLayouts(RecordingLayoutProvider layouts) : ITypeLayoutProvider
    {
        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => throw new InvalidOperationException();
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            layout = default;
            return false;
        }
        public ObjectLayout GetObjectLayout(EntityKey type) => layouts.GetObjectLayout(type);
        public int StringTypeId => layouts.StringTypeId;
        public int TypeTypeId => layouts.TypeTypeId;
    }

    private sealed class Descriptors(bool nullable) : ITypeDescriptorSource
    {
        internal static readonly CliTypeIdentity Underlying = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        private static readonly CliTypeIdentity Nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new AssemblyIdentity("NetWasm.CoreLib"), "System", "Nullable`1", true), [Underlying]);
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => nullable
            ? [new(Underlying, 12, 0, 16, 0, 0, null), new(Nullable, 27, 0, 16, 0, 0, null)]
            : [];
    }

    private sealed class RecordingReceiver : IArrayReceiverValidator
    {
        public int? Local { get; private set; }
        public IWasmInstructionWriter? Writer { get; private set; }
        public int InstructionCount { get; private set; }

        public void Validate(IWasmInstructionWriter code, int receiverLocal, OptionalFunctionIndex throwFunction)
        {
            Local = receiverLocal;
            Writer = code;
            InstructionCount = Assert.IsType<RecordingInstructionWriter>(code).ToInstructions().Length;
        }
    }

    private sealed class RecordingRoots : IRootPublicationEmitter
    {
        public InstructionEmissionRequest? Request { get; private set; }
        public IWasmInstructionWriter? Writer { get; private set; }
        public int InstructionCount { get; private set; }

        public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, bool constructorCall = false)
        {
            Assert.False(constructorCall);
            Request = request;
            Writer = code;
            InstructionCount = Assert.IsType<RecordingInstructionWriter>(code).ToInstructions().Length;
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
