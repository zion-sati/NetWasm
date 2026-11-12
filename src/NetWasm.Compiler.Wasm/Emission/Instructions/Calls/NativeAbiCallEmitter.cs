using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

// One call through the C ABI: a DllImport's (Import), or an unmanaged calli's through its
// trampoline, which takes the function pointer after the arguments (issue 226). The arguments are
// the stack's tail from ArgumentBase, the pointer (when there is one) last.
internal sealed record NativeAbiCall(
    InstructionEmissionRequest Instruction,
    NativeAbiSignaturePlan Lowering,
    int ArgumentBase,
    int Consumed,
    bool HostCall,
    string Method)
{
    // A DllImport's method: the resolver gives its function, and its declaring type is initialized first.
    public MethodInstanceModel? Import { get; init; }

    public NativeCallTrampoline? Trampoline { get; init; }
}

internal interface INativeAbiCallEmitter
{
    void Emit(NativeAbiCall call, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices);
}

internal sealed class NativeAbiCallEmitter(
    ITargetLayout layouts,
    IStaticInitializationEmitter initialization,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : INativeAbiCallEmitter
{
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IStaticInitializationEmitter _initialization = initialization ??
        throw new ArgumentNullException(nameof(initialization));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));

    public void Emit(NativeAbiCall call, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(functionIndices);
        var instruction = call.Instruction;
        var plan = call.Lowering;
        var functionPointer = call.Trampoline is not null;
        if ((call.Import is null) != functionPointer || call.ArgumentBase < 0 ||
            call.Consumed != plan.Parameters.Length + (functionPointer ? 1 : 0) ||
            call.ArgumentBase != instruction.Stack.Count - call.Consumed)
            throw Invariant("A native call requires the exact argument tail.");
        var frame = instruction.Context.ValueLayout;
        var result = plan.Result;
        var resultOffset = 0;
        // A vector value, as Vector4, is an aggregate in memory to native code.
        if (result.LogicalType.StackKind is CliValueKind.ValueType or CliValueKind.V128)
        {
            if (!frame.TemporaryOffsets.TryGetValue(instruction.Instruction.Offset, out resultOffset))
                throw Invariant("A native aggregate result has no planned storage.");
            ValidateStorage(result, resultOffset);
        }
        foreach (var parameter in plan.Parameters)
        {
            var slot = call.ArgumentBase + parameter.LogicalIndex;
            if (instruction.Stack[slot] != parameter.Value.LogicalType.StackKind)
                throw Invariant("A native argument does not match its logical stack representation.");
            if (parameter.Value.Kind == NativeAbiValueKind.IndirectAggregate)
                ValidateStorage(parameter.Value, ArgumentOffset(parameter));
        }
        if (functionPointer && instruction.Stack[^1] != CliValueKind.NativeInt)
            throw Invariant("An unmanaged calli requires a native-int function pointer.");
        int functionIndex;
        if (call.Import is { } import)
        {
            functionIndex = functionIndices.Resolve(import);
            _initialization.Emit(new(import.Definition.DeclaringType, import.DeclaringType,
                instruction.Target.StaticInitializerGuards, IsMethodInvocation: true), code, functionIndices);
        }
        else
            functionIndex = call.Trampoline!.Index.Value;
        foreach (var parameter in plan.Parameters)
        {
            if (parameter.Value.Kind != NativeAbiValueKind.IndirectAggregate)
                continue;
            EmitFrameAddress(ArgumentOffset(parameter));
            EmitArgument(parameter);
            if (parameter.Value.LogicalType.StackKind == CliValueKind.V128)
            {
                ManagedMemoryEmitter.EmitVectorStore(code, 0, parameter.Value.Size, scratch: instruction.Context.VectorScratch);
                continue;
            }
            _addresses.Emit(code, parameter.Value.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        // For a singleton result the address stays below the call operands, so
        // its scalar result can be stored without adding a scratch scalar local.
        if (result.Kind == NativeAbiValueKind.ScalarizedAggregate)
            EmitFrameAddress(resultOffset);
        // A host supplies a [WasmImportLinkage] function, so anything it throws
        // unwinds to a catch_all and raises JSException:
        //   block (result t)  block  try_table (result t) (catch_all 0)
        //     arguments, call  end  br 1  end  throw JSException  end
        var callResult = plan.PhysicalSignature.ReturnType == CliValueKind.Void
            ? WasmOpcodes.EmptyBlockType
            : (byte)WasmValueTypes.FromCli(plan.PhysicalSignature.ReturnType, _layouts.Target);
        if (call.HostCall)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(callResult)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.TryTable, WasmInstructionOperand.TryTableCatchAll(callResult, 0)));
        }
        if (result.Kind == NativeAbiValueKind.IndirectAggregate)
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
        if (functionPointer)
        {
            var slot = instruction.Stack.Count - 1;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)StackLocal(instruction.Context, slot, CliValueKind.NativeInt))));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndex)));
        if (call.HostCall)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            _exceptions.Emit(code, ManagedExceptionKind.JSException, instruction.Context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
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
        instruction.Stack.RemoveRange(call.ArgumentBase, call.Consumed);
        if (result.LogicalType.StackKind != CliValueKind.Void)
        {
            if (result.LogicalType.StackKind == CliValueKind.ValueType)
                EmitFrameAddress(resultOffset);
            else if (result.LogicalType.StackKind == CliValueKind.V128)
            {
                EmitFrameAddress(resultOffset);
                ManagedMemoryEmitter.EmitVectorLoad(code, 0, result.Size, scratch: instruction.Context.VectorScratch);
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)StackLocal(instruction.Context,
                    call.ArgumentBase, result.LogicalType.StackKind))));
            instruction.Stack.Add(result.LogicalType.StackKind);
        }

        CompilerException Invariant(string message) => new(new(DiagnosticCode.CompilerInvariant,
            message, call.Method, instruction.Instruction.Offset));

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
            var slot = call.ArgumentBase + parameter.LogicalIndex;
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
