using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedMethodBodyEmitterFactory(
    IManagedMethodEmitter managedMethods,
    IManagedMethodSequenceEmitter sequences,
    IStackTraceMethodIdProvider stackTraceMethodIds,
    ILogger<WasmModuleEmitterFactory> logger) : IManagedMethodBodyEmitterFactory
{
    public IManagedMethodBodyEmitter Create() =>
        new ManagedMethodBodyDiagnosticDecorator(
            new ManagedMethodBodyEmitter(
                managedMethods,
                sequences,
                stackTraceMethodIds),
            logger);
}
