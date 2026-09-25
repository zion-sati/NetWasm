using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class StaticInitializationEmitter(
    ITypeRepository types,
    IMethodRepository methods,
    IAddressInstructionEmitter addresses) : IStaticInitializationEmitter
{
    public void Emit(StaticInitializationEmissionRequest request,
        IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        var definition = types.GetTypeDefinition(request.TypeDefinition);
        // BeforeFieldInit owners initialize on field access, even when another
        // reachable field access has already caused their guard to be planned.
        if (request.IsStaticMethodCall && definition.IsBeforeFieldInit) return;
        var initializer = definition.Methods
            .Select(methods.GetMethod).SingleOrDefault(method => method.Name == ".cctor");
        if (initializer is null) return;
        var key = request.DeclaringType?.Shape == CliTypeShape.GenericInstantiation
            ? $"{request.DeclaringType.CanonicalName}::0x{initializer.Key.MetadataToken:x8}"
            : StaticInitializerGuard.KeyFor(initializer.Key);
        if (!request.ModuleData.StaticInitializerGuards.TryGetValue(key, out var guard)) return;

        addresses.Emit(code, guard.Address);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(2)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)guard.FunctionIndex.Value)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
