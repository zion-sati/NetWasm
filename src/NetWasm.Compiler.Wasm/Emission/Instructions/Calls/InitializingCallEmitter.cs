using System;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class InitializingCallEmitter(
    ICallEmitter inner,
    IMethodInitializationTriggerClassifier triggers,
    IStaticInitializationEmitter initialization) : ICallEmitter
{
    private readonly ICallEmitter _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IMethodInitializationTriggerClassifier _triggers = triggers ??
        throw new ArgumentNullException(nameof(triggers));
    private readonly IStaticInitializationEmitter _initialization = initialization ??
        throw new ArgumentNullException(nameof(initialization));

    public void Emit(CallEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(functionIndices);
        if (_triggers.Classify(request.Method.Definition))
        {
            _initialization.Emit(new(request.Method.Definition.DeclaringType,
                request.Method.DeclaringType, request.Instruction.Target.StaticInitializerGuards,
                IsMethodInvocation: true), code, functionIndices);
        }
        _inner.Emit(request, code, functionIndices);
    }
}
