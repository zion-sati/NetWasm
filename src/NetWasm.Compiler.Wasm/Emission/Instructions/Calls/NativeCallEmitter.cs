using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class NativeCallEmitter(
    ITargetLayout layouts,
    IStaticInitializationEmitter initialization,
    IAddressInstructionEmitter addresses) : ICallEmitter
{
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IStaticInitializationEmitter _initialization = initialization ??
        throw new ArgumentNullException(nameof(initialization));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));

    public void Emit(CallEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(functionIndices);
        var instruction = request.Instruction;
        if (instruction.Instruction.Operation != CilOperation.Call ||
            request.Consumed != request.Method.Signature.ParameterTypes.Length ||
            request.ArgumentBase < 0 ||
            request.ArgumentBase != instruction.Stack.Count - request.Consumed)
            throw new CompilerException(new(DiagnosticCode.CompilerInvariant,
                "A native call requires the exact static call argument tail.", request.Method.CanonicalName));
        if (!instruction.Target.NativeImports.ByMethod.TryGetValue(request.Method.Definition.Key, out var native) ||
            !native.Method.HasEquivalentDescriptorFacts(request.Method))
            throw Invariant("A native call has no matching published ABI plan.");
        var plan = native.Abi.Lowering;
        var frame = instruction.Context.ValueLayout;
        var result = plan.Result;
        var resultOffset = 0;
        if (result.LogicalType.StackKind == CliValueKind.ValueType)
        {
            if (!frame.TemporaryOffsets.TryGetValue(instruction.Instruction.Offset, out resultOffset))
                throw Invariant("A native aggregate result has no planned storage.");
            ValidateStorage(result, resultOffset);
        }
        foreach (var parameter in plan.Parameters)
        {
            var slot = request.ArgumentBase + parameter.LogicalIndex;
            if (instruction.Stack[slot] != parameter.Value.LogicalType.StackKind)
                throw Invariant("A native argument does not match its logical stack representation.");
            if (parameter.Value.Kind == NativeAbiValueKind.IndirectAggregate)
                ValidateStorage(parameter.Value, ArgumentOffset(parameter));
        }
        var functionIndex = functionIndices.Resolve(request.Method);
        _initialization.Emit(new(request.Method.Definition.DeclaringType,
            request.Method.DeclaringType, instruction.Target.ModuleData, IsStaticMethodCall: true),
            code, functionIndices);
        foreach (var parameter in plan.Parameters)
        {
            if (parameter.Value.Kind != NativeAbiValueKind.IndirectAggregate)
                continue;
            EmitFrameAddress(ArgumentOffset(parameter));
            EmitArgument(parameter);
            _addresses.Emit(code, parameter.Value.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        // For a singleton result the address stays below the call operands, so
        // its scalar result can be stored without adding a scratch scalar local.
        if (result.Kind is NativeAbiValueKind.ScalarizedAggregate or NativeAbiValueKind.IndirectAggregate)
            EmitFrameAddress(resultOffset);
        foreach (var parameter in plan.Parameters)
        {
            if (parameter.Value.Kind == NativeAbiValueKind.IgnoredAggregate)
                continue;
            if (parameter.Value.Kind == NativeAbiValueKind.IndirectAggregate)
                EmitFrameAddress(ArgumentOffset(parameter));
            else
            {
                EmitArgument(parameter);
                if (parameter.Value.Kind == NativeAbiValueKind.ScalarizedAggregate)
                    ManagedMemoryEmitter.EmitLoadByType(code, _layouts.Target, parameter.Value.ScalarOffset,
                        parameter.Value.ScalarStorageType!, parameter.Value.Size);
            }
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndex)));
        if (result.Kind == NativeAbiValueKind.ScalarizedAggregate)
            ManagedMemoryEmitter.EmitStoreByType(code, _layouts.Target, result.ScalarOffset,
                result.ScalarStorageType!, result.Size);
        else if (result.Kind == NativeAbiValueKind.IgnoredAggregate)
        {
            EmitFrameAddress(resultOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            _addresses.Emit(code, result.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        }
        instruction.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        if (result.LogicalType.StackKind != CliValueKind.Void)
        {
            if (result.LogicalType.StackKind == CliValueKind.ValueType)
                EmitFrameAddress(resultOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)StackLocal(instruction.Context,
                    request.ArgumentBase, result.LogicalType.StackKind))));
            instruction.Stack.Add(result.LogicalType.StackKind);
        }

        CompilerException Invariant(string message) => new(new(DiagnosticCode.CompilerInvariant,
            message, request.Method.CanonicalName, instruction.Instruction.Offset));

        int ArgumentOffset(NativeAbiParameterPlan parameter) =>
            frame.NativeArgumentOffsets.TryGetValue((instruction.Instruction.Offset, parameter.LogicalIndex), out var offset)
                ? offset : throw Invariant("A native aggregate argument has no private copy storage.");

        void ValidateStorage(NativeAbiValuePlan value, int offset)
        {
            if (value.Size <= 0 || value.Alignment <= 0 || value.Alignment > 16 ||
                (value.Alignment & (value.Alignment - 1)) != 0 || offset < 0 ||
                offset % value.Alignment != 0 || offset > frame.Size - value.Size)
                throw Invariant("A native aggregate has invalid planned frame storage.");
        }

        void EmitArgument(NativeAbiParameterPlan parameter)
        {
            var slot = request.ArgumentBase + parameter.LogicalIndex;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)StackLocal(instruction.Context, slot, instruction.Stack[slot]))));
        }

        void EmitFrameAddress(int offset)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)instruction.Context.ValueFrame)));
            if (offset == 0)
                return;
            _addresses.Emit(code, offset);
            _addresses.Emit(code, AddressOperation.Add);
        }
    }

    private int StackLocal(MethodEmissionContext context, int slot, CliValueKind kind) =>
        WasmLocalLayoutPlanner.GetEvaluationStackLocal(context.StackLocals, slot, kind, _layouts.Target);
}
