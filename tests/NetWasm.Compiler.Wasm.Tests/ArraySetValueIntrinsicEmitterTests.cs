using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArraySetValueIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void PassesExactStorageArgumentsAndNullableLayout(WasmTarget target, bool nullable)
    {
        var layout = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var addresses = new Addresses();
        var descriptors = new Descriptors(nullable);
        var resolver = new NullableResolver();
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ArraySetValue,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedReference]) with
        { Target = WasmTargetLayout.For(target) };
        var writer = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(new ArraySetValueIntrinsicEmitter(
            descriptors, resolver, layout, layout, layout, imports, addresses));
        emitter.Emit(request, writer);
        var code = writer.ToInstructions();
        Assert.Equal((uint)layout.ArrayElementTypeIdOffset, code[1].Operand.Offset);
        Assert.Equal<int>(nullable ? [0, 4] : [0], addresses.Constants);
        Assert.Equal(nullable ? 1 : 0, code.Count(i => i.Opcode == WasmOpcodes.If));
        Assert.Equal(nullable ? Descriptors.Underlying : null, layout.ObjectIdentityRequest);
        var tail = code.TakeLast(7).ToArray();
        Assert.Equal(Enumerable.Repeat(WasmOpcodes.LocalGet, 5).Concat([WasmOpcodes.Call, WasmOpcodes.LocalSet]),
            tail.Select(i => i.Opcode));
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), tail[0].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(1, CliValueKind.I4), tail[1].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(2, CliValueKind.ManagedReference), tail[2].Operand.UnsignedValue);
        Assert.Equal((uint)request.Instruction.Context.NumericTemporaryI4Second, tail[3].Operand.UnsignedValue);
        Assert.Equal((uint)request.Instruction.Context.ObjectTemporary, tail[4].Operand.UnsignedValue);
        Assert.Equal((uint)imports.Resolve(RuntimeImportSymbol.ArraySetValue,
            request.Instruction.Target.RuntimeImportSelection), tail[5].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.I4), tail[6].Operand.UnsignedValue);
    }
    private sealed class Addresses : IAddressInstructionEmitter
    {
        public List<int> Constants { get; } = [];
        public void Emit(IWasmInstructionWriter code, int constant) => Constants.Add(constant);
        public void Emit(IWasmInstructionWriter code, AddressOperation operation) => throw new InvalidOperationException();
    }
    private sealed class Descriptors(bool nullable) : ITypeDescriptorSource
    {
        public static readonly CliTypeIdentity Underlying = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        public static readonly CliTypeIdentity Nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new AssemblyIdentity("NetWasm.CoreLib"), "System", "Nullable`1", true), [Underlying]);
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => nullable
            ? [new(Underlying, 12, 0, 16, [], null), new(Nullable, 27, 0, 16, [], null)]
            : [];
    }
    private sealed class NullableResolver : INullableTypeResolver
    {
        public CliTypeIdentity? Resolve(CliTypeIdentity type) => type == Descriptors.Nullable ? Descriptors.Underlying : null;
    }
}
