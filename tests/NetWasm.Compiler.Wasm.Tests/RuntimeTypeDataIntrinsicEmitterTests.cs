using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class RuntimeTypeDataIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UninitializedObjectsAllocateZeroedStorageThroughTheFinalizableAllocatorWhenNeeded(WasmTarget target)
    {
        var plain = CliTypeIdentity.Named(new AssemblyIdentity("fixtures"), "Fixtures", "Plain", isValueType: false);
        var finalizable = CliTypeIdentity.Named(new AssemblyIdentity("fixtures"), "Fixtures", "Finalizable", isValueType: false);
        var layouts = new Layouts(new() { [plain] = new(31, 24, []), [finalizable] = new(32, 16, []) });
        var roots = new RecordingRoots();
        var addresses = new RecordingAddresses();
        var exceptions = new RecordingExceptions();
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.AllocateUninitializedObject, [CliValueKind.I4]) with
        {
            Target = WasmTargetLayout.For(target),
        };
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with { FinalizableTypeIds = Lookups.Of(ImmutableHashSet.Create(32)) },
                },
            },
        };
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(new AllocateUninitializedObjectIntrinsicEmitter(
            new Source([plain, finalizable], []), layouts, imports, roots, addresses, exceptions));
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var code = writer.ToInstructions();
        Assert.Same(request.Instruction, roots.Request);
        Assert.Equal([0, 24, 16], addresses.Constants);
        Assert.Equal([AddressOperation.EqualZero, AddressOperation.EqualZero], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.OutOfMemory], exceptions.Kinds);
        var selection = request.Instruction.Target.RuntimeImportSelection;
        Assert.Equal(
            [(uint)imports.Resolve(RuntimeImportSymbol.Allocate, selection),
                (uint)imports.Resolve(RuntimeImportSymbol.AllocateFinalizable, selection)],
            code.Where(i => i.Opcode == WasmOpcodes.Call).Select(i => i.Operand.UnsignedValue));
        Assert.Equal(2, code.Count(i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue == 31));
        Assert.Equal(2, code.Count(i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue == 32));
        Assert.Equal(2, code.Count(i => i.Opcode == WasmOpcodes.LocalTee &&
            i.Operand.UnsignedValue == (uint)request.Local(0, CliValueKind.ManagedReference)));
    }

    [Fact]
    public void StructureDescriptorsResolveByTypeIdWithZeroForUnknownTypes()
    {
        var point = CliTypeIdentity.Named(new AssemblyIdentity("fixtures"), "Fixtures", "Point", isValueType: true);
        var mixed = CliTypeIdentity.Named(new AssemblyIdentity("fixtures"), "Fixtures", "Mixed", isValueType: true);
        var addresses = new RecordingAddresses();
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.StructMarshallingData, [CliValueKind.I4]);
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(new StructMarshallingDataIntrinsicEmitter(
            new Source([], [new(point, 41, 1000), new(mixed, 42, 2000)]), addresses));
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var code = writer.ToInstructions();
        Assert.Equal([0, 1000, 2000], addresses.Constants);
        Assert.Equal([41, 42], code.Where(i => i.Opcode == WasmOpcodes.I32Constant).Select(i => i.Operand.SignedValue));
        Assert.Equal(3, code.Count(i => i.Opcode == WasmOpcodes.LocalSet &&
            i.Operand.UnsignedValue == (uint)request.Local(0, CliValueKind.NativeInt)));
        Assert.Equal(2, code.Count(i => i.Opcode == WasmOpcodes.If));
    }

    private sealed class Source(
        ImmutableArray<CliTypeIdentity> types,
        ImmutableArray<StructMarshallingLayout> descriptors) : IUninitializedObjectSource, IStructMarshallingSource
    {
        public ImmutableArray<CliTypeIdentity> UninitializedObjectTypes => types;
        public ImmutableArray<StructMarshallingLayout> StructMarshalling => descriptors;
    }

    private sealed class Layouts(Dictionary<CliTypeIdentity, ObjectLayout> objects) : ITypeLayoutProvider
    {
        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => objects[type];
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout) => objects.TryGetValue(type, out layout);
        public ObjectLayout GetObjectLayout(EntityKey type) => throw new NotSupportedException();
        public int StringTypeId => 13;
        public int TypeTypeId => 14;
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
