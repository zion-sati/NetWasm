using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class NativeIntegerConstructionEmitter(
    ITargetLayout layouts,
    INativeIntegerConversionEmitter conversions) : INativeIntegerConstructionEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly INativeIntegerConversionEmitter _conversions = conversions ??
        throw new ArgumentNullException(nameof(conversions));

    public void Emit(NativeIntegerConstructionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Signature.ParameterTypes.Length != 1)
        {
            throw new InvalidOperationException(
                "native integer construction requires one value");
        }

        var source = request.Emission.Stack[request.ArgumentBase];
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Emission.Context,
                request.ArgumentBase,
                source))));
        _conversions.Emit(
            code,
            source,
            IsUnsigned(request.Signature.ParameterSignatureTypes[0]));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Emission.Context,
                request.ArgumentBase,
                CliValueKind.NativeInt))));
        request.Emission.Stack.RemoveRange(
            request.ArgumentBase,
            request.Signature.ParameterTypes.Length);
        request.Emission.Stack.Add(CliValueKind.NativeInt);
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        _layouts.Target);

    private static bool IsUnsigned(CliTypeIdentity type) => type.CanonicalName is
        "primitive:u1" or
        "primitive:u2" or
        "primitive:u4" or
        "primitive:u8" or
        "primitive:nativeuint";
}
