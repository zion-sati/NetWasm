using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

/// <summary>
/// Turns a reference value that enters managed code from JavaScript, a string,
/// number array, JSObject or handle value arriving as a host handle, into its
/// managed object. Synchronous and asynchronous exports and callbacks share it, so a
/// boundary value has one conversion wherever it enters.
/// </summary>
internal interface IJavaScriptReferenceArgumentEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target,
        OptionalFunctionIndex throwFunction);
}

internal sealed class JavaScriptReferenceArgumentEmitter(
    IHostCallbackStringArgumentMarshaller strings,
    IHostCallbackByteArrayArgumentMarshaller valueArrays,
    IHostCallbackHostObjectArgumentMarshaller hostObjects) : IJavaScriptReferenceArgumentEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target,
        OptionalFunctionIndex throwFunction)
    {
        if (InteropTypeClassifier.IsString(type))
        {
            strings.Emit(code, handleParameter, destination, temporaryI4, objectTemporary, target, throwFunction);
        }
        else if (InteropTypeClassifier.IsJSObject(type))
        {
            hostObjects.Emit(code, type, handleParameter, destination, throwFunction);
        }
        else if (InteropTypeClassifier.IsValueArray(type))
        {
            valueArrays.Emit(code, type, handleParameter, destination, temporaryI4, objectTemporary, target,
                throwFunction);
        }
        else if (InteropTypeClassifier.IsHandleValue(type))
        {
            // CoreLib converts the JavaScript value; the caller keeps and releases its handle.
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)handleParameter)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)target.Imports.ToManaged(type))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)destination)));
        }
        else
        {
            throw new InvalidOperationException(
                $"'{type.CanonicalName}' does not enter managed code as a JavaScript reference.");
        }
    }
}
