using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumNullCheckEmitter
{
    // A null receiver throws NullReferenceException; a null argument the exception the method
    // documents, such as HasFlag's ArgumentNullException.
    void Emit(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction,
        ManagedExceptionKind exception = ManagedExceptionKind.NullReference);
}
