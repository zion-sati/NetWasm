using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class FilterSequenceEmitterAdapter(
    Func<IWasmInstructionWriter, StructuredMethod, StructuredSequence,
        MethodEmissionContext, ManagedMethodSequenceEmission> emitSequence) :
    IFilterSequenceEmitter
{
    private readonly Func<IWasmInstructionWriter, StructuredMethod, StructuredSequence,
        MethodEmissionContext, ManagedMethodSequenceEmission> _emitSequence =
        emitSequence ?? throw new ArgumentNullException(nameof(emitSequence));

    public ManagedMethodSequenceEmission Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredSequence sequence,
        MethodEmissionContext context) =>
        _emitSequence(code, method, sequence, context);
}
