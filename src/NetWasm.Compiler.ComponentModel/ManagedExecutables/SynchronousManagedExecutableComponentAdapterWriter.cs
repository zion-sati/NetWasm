using System;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed class SynchronousManagedExecutableComponentAdapterWriter(
    IWasmTextModuleWriter modules) : IManagedExecutableComponentAdapterWriter
{
    private readonly IWasmTextModuleWriter _modules = modules ??
        throw new ArgumentNullException(nameof(modules));

    public void Write(ManagedExecutableComponentAdapterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.EntryPoint);
        if (request.EntryPoint.CompletionShape ==
            ManagedExecutableCompletionShape.Asynchronous)
        {
            throw ComponentException.Invalid(
                "asynchronous managed entry points require the NetWasm process observation protocol, which the synchronous WASI CLI adapter does not yet expose");
        }

        _ = request.EntryPoint.ParameterShape switch
        {
            ManagedExecutableParameterShape.None => true,
            ManagedExecutableParameterShape.StringArray => true,
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.EntryPoint.ParameterShape,
                "unsupported managed executable parameter shape"),
        };
        var result = request.EntryPoint.ReturnShape switch
        {
            ManagedExecutableReturnShape.Void => string.Empty,
            ManagedExecutableReturnShape.ExitCode => "(result i32)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.EntryPoint.ReturnShape,
                "unsupported managed executable return shape"),
        };
        var normalizeResult = request.EntryPoint.ReturnShape ==
            ManagedExecutableReturnShape.ExitCode
                ? "i32.eqz i32.eqz"
                : "i32.const 0";
        var module =
            "(module " +
            $"(import \"netwasm.application.v1\" \"run\" (func $application_run {result})) " +
            "(func (export \"cm32p2|wasi:cli/run@0.2|run\") (result i32) " +
            $"call $application_run {normalizeResult}))";
        if (request.Target.Width == "wasm64")
        {
            module = module
                .Replace("cm32p2", "cm64p2", StringComparison.Ordinal);
        }
        _modules.Write(module, request.OutputPath);
    }
}
