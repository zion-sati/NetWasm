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

public sealed class ArrayCreateInstanceIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void EmitsExactAllocatorArgumentsAndAllocationFailureChecks(WasmTarget target, bool value)
    {
        var layout = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var element = value ? CliTypeIdentity.FromStackKind(CliValueKind.I4) : CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
        var descriptors = new Descriptors(element);
        var roots = new RecordingRoots(); var addresses = new RecordingAddresses(); var exceptions = new RecordingExceptions();
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ArrayCreateInstance, [CliValueKind.I4, CliValueKind.I4]) with { Target = WasmTargetLayout.For(target) };
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(new ArrayCreateInstanceIntrinsicEmitter(descriptors, layout, layout, imports, roots, addresses, exceptions));
        var writer = new RecordingInstructionWriter();
        emitter.Emit(request, writer);
        var code = writer.ToInstructions();
        Assert.Same(request.Instruction, roots.Request); Assert.Equal(2, roots.InstructionCount);
        Assert.Equal([0], addresses.Constants); Assert.Equal([AddressOperation.EqualZero], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.OutOfMemory], exceptions.Kinds);
        Assert.Equal(element, layout.ObjectIdentityRequest);
        var call = Assert.Single(code.Where(i => i.Opcode == WasmOpcodes.Call));
        Assert.Equal((uint)imports.Resolve(value ? RuntimeImportSymbol.AllocateValueArray : RuntimeImportSymbol.AllocateReferenceArray, request.Instruction.Target.RuntimeImportSelection), call.Operand.UnsignedValue);
        Assert.Equal(2, code.Count(i => i.Opcode == WasmOpcodes.If));
        Assert.Contains(code, i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue == 77);
        Assert.Contains(code, i => i.Opcode == WasmOpcodes.LocalGet && i.Operand.UnsignedValue == (uint)request.Local(1, CliValueKind.I4));
        Assert.Equal((uint)request.Instruction.Context.NumericTemporaryI4, code[1].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), Assert.Single(code.Where(i => i.Opcode == WasmOpcodes.LocalTee)).Operand.UnsignedValue);
    }
    // A vector whose element type has no layout, such as a parameter's DynamicMetaObject[] when no
    // DynamicMetaObject is ever constructed, gets no allocation branch: no runtime Type names its element.
    [Fact]
    public void SkipsVectorsWhoseElementTypeHasNoLayout()
    {
        var layout = new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32));
        var element = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
        var hidden = CliTypeIdentity.Named(new AssemblyIdentity("in-memory"), "Fixtures", "Hidden", isValueType: false);
        var types = new PartialLayouts(layout, hidden);
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ArrayCreateInstance, [CliValueKind.I4, CliValueKind.I4]);
        var imports = WasmRuntimeImports.CreateCatalog();
        var writer = new RecordingInstructionWriter();
        new ArrayCreateInstanceIntrinsicEmitter(new Descriptors(element, hidden), types, layout, imports,
            new RecordingRoots(), new RecordingAddresses(), new RecordingExceptions()).Emit(request, writer);
        var code = writer.ToInstructions();

        Assert.Single(code.Where(i => i.Opcode == WasmOpcodes.Call));
        Assert.Contains(code, i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue == 77);
        Assert.DoesNotContain(code, i => i.Opcode == WasmOpcodes.I32Constant && i.Operand.SignedValue == 78);
    }

    private sealed class PartialLayouts(RecordingLayoutProvider layouts, CliTypeIdentity missing) : ITypeLayoutProvider
    {
        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => layouts.GetObjectLayout(type);
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            if (type == missing)
            {
                layout = default;
                return false;
            }
            return layouts.GetObjectLayout(type, out layout);
        }
        public ObjectLayout GetObjectLayout(EntityKey type) => layouts.GetObjectLayout(type);
        public int StringTypeId => layouts.StringTypeId;
        public int TypeTypeId => layouts.TypeTypeId;
    }

    private sealed class Descriptors(CliTypeIdentity element, CliTypeIdentity? unlaidOut = null) : ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [
            new(element, 1, 0, 16, [], null),
            new(CliTypeIdentity.SzArray(CliTypeIdentity.GenericParameter(false, 0)), 2, 0, 16, [], null),
            new(CliTypeIdentity.SzArray(element), 77, 0, 16, [], null),
            .. unlaidOut is { } other ? [new ConstructedTypeDescriptorLayout(CliTypeIdentity.SzArray(other), 78, 0, 16, [], null)] : Array.Empty<ConstructedTypeDescriptorLayout>()];
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
