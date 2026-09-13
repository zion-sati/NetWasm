using System;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class FilterSequenceEmitterFactory(
    IManagedMethodSequenceEmitter sequences) : IFilterSequenceEmitterFactory
{
    private readonly IManagedMethodSequenceEmitter _sequences = sequences ??
        throw new ArgumentNullException(nameof(sequences));

    public IFilterSequenceEmitter Create(
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);

        return new FilterSequenceEmitterAdapter(
            (code, method, body, context) => _sequences.Emit(
                code,
                method,
                body,
                context,
                target,
                functionIndices));
    }
}
