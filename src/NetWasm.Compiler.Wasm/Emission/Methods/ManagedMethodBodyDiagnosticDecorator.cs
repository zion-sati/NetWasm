using System;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedMethodBodyDiagnosticDecorator(
    IManagedMethodBodyEmitter inner,
    ILogger<WasmModuleEmitterFactory> logger) :
    IManagedMethodBodyEmitter
{
    private static readonly Action<ILogger, string, string, Exception?> LogStarted =
        LoggerMessage.Define<string, string>(
            LogLevel.Trace,
            new EventId(4300, nameof(LogStarted)),
            "Managed method emission started {MethodName} {EmissionIdentity}.");

    private static readonly Action<ILogger, string, string, Exception?> LogCompleted =
        LoggerMessage.Define<string, string>(
            LogLevel.Trace,
            new EventId(4301, nameof(LogCompleted)),
            "Managed method emission completed {MethodName} {EmissionIdentity}.");

    private static readonly Action<ILogger, string, string, Exception?> LogFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(4302, nameof(LogFailed)),
            "Managed method emission failed {MethodName} {EmissionIdentity}.");

    public ManagedMethodBodyEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        MethodInstanceModel? methodInstance = null)
    {
        ArgumentNullException.ThrowIfNull(structured);

        var identity = ManagedMethodBodyKey.Resolve(
            structured);
        LogStarted(logger, method.Name, identity, null);
        try
        {
            var emission = inner.Emit(
                method,
                callerIdentity,
                structured,
                rootMap,
                target,
                functionIndices,
                methodInstance);
            LogCompleted(logger, method.Name, identity, null);
            return emission;
        }
        catch (Exception exception)
        {
            LogFailed(logger, method.Name, identity, exception);
            throw;
        }
    }
}
