using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// Methods throw by calling these functions, so no ordinary method holds a throw
// instruction. managed.throw begins a fresh dispatch; managed.rethrow keeps the stack
// trace an ExceptionDispatchInfo captured. A failed check passes managed.throw its
// exception kind's tag instead of an object; managed.throw calls the kind's getter
// in CoreLib's RuntimeExceptionHelpers and throws the exception it returns.
internal sealed class ManagedThrowFunctionAppender(
    IGeneratedFunctionWriterFactory writers,
    IRuntimeImportResolver runtimeImports,
    ITargetLayout layouts,
    IMethodRepository methodRepository,
    ITypeRepository types) : IManagedThrowFunctionAppender
{
    private readonly IGeneratedFunctionWriterFactory _writers = writers ??
        throw new ArgumentNullException(nameof(writers));
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IMethodRepository _methodRepository = methodRepository ??
        throw new ArgumentNullException(nameof(methodRepository));
    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));

    public void Append(
        IList<WasmFunctionDefinition> functions,
        OptionalFunctionIndex throwFunctionIndex,
        OptionalFunctionIndex rethrowFunctionIndex,
        ImmutableArray<ManagedDefinitionEmission> methods,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(functionIndices);
        if (throwFunctionIndex.IsPresent)
        {
            functions.Add(Create("managed.throw", RuntimeImportSymbol.BeginThrow, Getters(methods, functionIndices)));
        }
        if (rethrowFunctionIndex.IsPresent)
        {
            functions.Add(Create("managed.rethrow", RuntimeImportSymbol.BeginRethrow, new Dictionary<int, int>()));
        }
    }

    private WasmFunctionDefinition Create(string name, RuntimeImportSymbol begin, Dictionary<int, int> getters)
    {
        var code = _writers.Create();
        code.Bytes.Write([0]);
        if (getters.Count != 0) EmitGetters(code.Instructions, getters);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_runtimeImports.Resolve(begin))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return new(
            name,
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.ManagedReference),
            code.Snapshots.Read())
        {
            IsNoInlining = true,
        };
    }

    // Each reachable getter's function, by its kind's tag.
    private Dictionary<int, int> Getters(ImmutableArray<ManagedDefinitionEmission> methods, IFunctionIndexResolver functionIndices)
    {
        var tags = Enum.GetValues<ManagedExceptionKind>()
            .ToDictionary(RuntimeExceptionGetters.MethodName, RuntimeExceptionGetters.Tag, StringComparer.Ordinal);
        var getters = new Dictionary<int, int>();
        foreach (var method in methods)
        {
            var definition = _methodRepository.GetMethod(method.MethodKey);
            if (tags.TryGetValue(definition.Name, out var tag) &&
                _types.GetTypeDefinition(definition.DeclaringType).FullName == RuntimeExceptionGetters.TypeName)
            {
                getters.Add(tag, functionIndices.Resolve(method.MethodKey));
            }
        }
        return getters;
    }

    // tag - 1 < TagLimit - 1, unsigned, takes tags 1 to TagLimit - 1 and no
    // object. The tag's getter replaces it with the exception to throw.
    private void EmitGetters(IWasmInstructionWriter code, Dictionary<int, int> getters)
    {
        var wide = _layouts.Target.UsesMemory64;
        void Constant(int value) => code.Write(wide
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        void Argument() => code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(0)));
        Argument();
        Constant(1);
        code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract));
        Constant(RuntimeExceptionGetters.TagLimit - 1);
        code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64LessThanUnsigned : WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var (tag, getter) in getters.OrderBy(static pair => pair.Key))
        {
            Argument();
            Constant(tag);
            code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)getter)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned(0)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
