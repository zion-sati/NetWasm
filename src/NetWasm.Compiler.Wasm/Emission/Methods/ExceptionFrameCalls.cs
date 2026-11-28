// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// A runtime call of a registered exception frame (the runtime's exception_frame_*).
internal enum ExceptionFrameCall
{
    // Registers the block's frame and pushes its token.
    Enter,

    // Gives the frame whose token is in the group's frame local its filter environment, when it has filters.
    SetEnvironment,

    // Pops the frame.
    Leave,

    // Rewinds the frame arenas to where the block began and pops the frame.
    Unwind,

    // Pushes the clause the first pass selected in the frame (from 1), or 0.
    TargetClause,

    // The exception is no longer in flight.
    EndCatch,

    // Pushes whether a frame with filter clauses is registered.
    FilterFramesLive,

    // At a landing pad, pushes the first of the group's catch clauses the exception (ExceptionTemporary) is assignable
    // to, from 1, or 0.
    SelectCatch,
}

internal interface IExceptionFrameCallEmitter
{
    void Emit(IWasmInstructionWriter code, ExceptionFrameCall call, ExceptionFrameRequest request);
}

internal sealed class ExceptionFrameCallEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IStructuredExceptionGroupKeyFactory exceptionGroupKeys) : IExceptionFrameCallEmitter
{
    public void Emit(IWasmInstructionWriter code, ExceptionFrameCall call, ExceptionFrameRequest request)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        var token = request.Context.ExceptionFrameLocals[request.Group.Id];
        switch (call)
        {
            case ExceptionFrameCall.Enter:
                Clauses(code, request);
                Call(code, RuntimeImportSymbol.ExceptionFrameEnter);
                break;
            // These come after imports a program selects, so their indices follow its selection.
            case ExceptionFrameCall.FilterFramesLive:
                Call(code, RuntimeImportSymbol.ExceptionFilterFramesLive, request.Context.RuntimeImportSelection);
                break;
            case ExceptionFrameCall.SelectCatch:
                Get(code, request.Context.ExceptionTemporary);
                Clauses(code, request);
                Call(code, RuntimeImportSymbol.ExceptionCatchSelect, request.Context.RuntimeImportSelection);
                break;
            case ExceptionFrameCall.SetEnvironment:
                if (!Metadata(request).HasFilters) break;
                Get(code, token);
                Get(code, request.Context.ValueFrame);
                Call(code, RuntimeImportSymbol.ExceptionFrameSetEnvironment);
                break;
            case ExceptionFrameCall.Leave:
                Get(code, token);
                Call(code, RuntimeImportSymbol.ExceptionFrameLeave);
                break;
            case ExceptionFrameCall.Unwind:
                Get(code, token);
                Call(code, RuntimeImportSymbol.ExceptionFrameUnwind);
                break;
            case ExceptionFrameCall.TargetClause:
                Get(code, token);
                Call(code, RuntimeImportSymbol.ExceptionFrameTargetClause);
                break;
            case ExceptionFrameCall.EndCatch:
                Call(code, RuntimeImportSymbol.EndCatch);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(call));
        }
    }

    // The group's clause metadata: its address and its clause count.
    private void Clauses(IWasmInstructionWriter code, ExceptionFrameRequest request)
    {
        var metadata = Metadata(request);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(metadata.Address)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(metadata.Address)));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(metadata.ClauseCount)));
    }

    private ExceptionGroupMetadata Metadata(ExceptionFrameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Metadata);
        return request.Metadata.Required(exceptionGroupKeys.Create(request.Method, request.Group.Id));
    }

    private void Call(IWasmInstructionWriter code, RuntimeImportSymbol symbol) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(symbol))));

    private void Call(IWasmInstructionWriter code, RuntimeImportSymbol symbol, RuntimeImportSelection selection) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(symbol, selection))));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
}
