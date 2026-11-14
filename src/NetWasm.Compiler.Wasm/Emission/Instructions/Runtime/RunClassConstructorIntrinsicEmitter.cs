using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// RuntimeHelpers.RunClassConstructor: the handle is a semantic type ID. The type whose ID
// matches runs its static-initializer guard exactly as a static access would, so the
// initializer runs once, recursion sees the in-progress state and a failure stays cached.
// Types without a compiled initializer need nothing.
internal sealed class RunClassConstructorIntrinsicEmitter(
    ITypeDescriptorSource descriptors,
    ITypeRepository types,
    ITypeDefinitionResolver definitions,
    IMethodRepository methods,
    ITypeLayoutProvider layouts,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var guards = request.Instruction.Target.StaticInitializerGuards;
        var handle = request.Local(0, CliValueKind.I4);
        var emitted = new HashSet<int>();
        foreach (var type in Candidates())
        {
            if (!layouts.GetObjectLayout(type, out var layout) ||
                Guard(type, guards) is not { } guard ||
                !emitted.Add(layout.TypeId))
            {
                continue;
            }
            Get(code, handle);
            I32(code, layout.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            addresses.Emit(code, guard.Address);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
            I32(code, 2);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)guard.FunctionIndex.Value)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    // Every type that has a semantic type ID can produce a handle.
    private IEnumerable<CliTypeIdentity> Candidates() =>
        descriptors.TypeDescriptors.Select(descriptor =>
                CliTypeIdentity.FromDefinition(types.GetTypeDefinition(descriptor.Type)))
            .Concat(descriptors.ConstructedTypeDescriptors.Select(descriptor => descriptor.Type))
            .Concat(descriptors.ValueTypeDescriptors.Select(descriptor => descriptor.Type))
            .Concat(descriptors.MetadataTypeDescriptors.Select(descriptor => descriptor.Type))
            .Where(type => !type.ContainsGenericParameters &&
                type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation);

    // Guards are keyed as StaticInitializationEmitter keys them.
    private StaticInitializerGuard? Guard(
        CliTypeIdentity type,
        IEntryLookup<string, StaticInitializerGuard> guards)
    {
        var initializer = definitions.ResolveTypeIdentity(type).Methods
            .Select(methods.GetMethod)
            .SingleOrDefault(method => method.Name == ".cctor");
        if (initializer is null)
            return null;
        var key = type.Shape == CliTypeShape.GenericInstantiation
            ? $"{type.CanonicalName}::0x{initializer.Key.MetadataToken:x8}"
            : StaticInitializerGuard.KeyFor(initializer.Key);
        return guards.TryGetValue(key, out var guard) ? guard : null;
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void I32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
