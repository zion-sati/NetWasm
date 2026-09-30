using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ObjectArrayDelegateAdapterIntrinsicEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider types,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots) : IRuntimeIntrinsicEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly ITypeLayoutProvider _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IRuntimeObjectLayout _objects = objects ??
        throw new ArgumentNullException(nameof(objects));
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));
    private readonly IRootPublicationEmitter _roots = roots ??
        throw new ArgumentNullException(nameof(roots));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        if (!request.Instruction.Target.ObjectArrayDelegateAdapters.TryGetValue(
                request.Method.CanonicalName,
                out var plan))
        {
            throw RuntimeContract(
                $"delegate adapter factory '{request.Method.CanonicalName}' has no plan");
        }
        Validate(request, plan);

        _roots.Emit(request.Instruction, code);
        if (!plan.IsSupported)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned(
                    (uint)request.FunctionIndices.Resolve(plan.Target))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
            return;
        }
        var layout = _types.GetObjectLayout(plan.DelegateType);
        _addresses.Emit(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(layout.TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)_runtimeImports.Resolve(RuntimeImportSymbol.Allocate))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Instruction.Context.ObjectTemporary)));
        EmitAllocationFailureCheck(
            code,
            request.Instruction.Context.ObjectTemporary);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Instruction.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Local(0, CliValueKind.ManagedReference))));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            _layouts.Target,
            _objects.DelegateTargetOffset,
            _layouts.Target.ObjectReferenceSize);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Instruction.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(
                request.FunctionIndices.Resolve(plan.Target))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(
                2,
                (uint)_objects.DelegateMethodIdOffset)));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Instruction.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Local(0, CliValueKind.ManagedReference))));
    }

    private static void Validate(
        RuntimeIntrinsicEmissionRequest request,
        ObjectArrayDelegateAdapterPlan plan)
    {
        if (!StringComparer.Ordinal.Equals(
                plan.Factory.CanonicalName,
                request.Method.CanonicalName) ||
            request.Method.Signature.ParameterTypes.AsSpan().SequenceEqual(
                [CliValueKind.ManagedReference]) is false ||
            (plan.IsSupported &&
                request.Method.Signature.ReturnType !=
                    CliValueKind.ManagedReference))
        {
            throw RuntimeContract(
                $"delegate adapter plan '{request.Method.CanonicalName}' contradicts " +
                "the runtime factory signature");
        }
    }

    private void EmitAllocationFailureCheck(
        IWasmInstructionWriter code,
        int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        _exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static CompilerException RuntimeContract(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.RuntimeContract, message));
}
