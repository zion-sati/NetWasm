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
        var addressType = request.Target.Width == "wasm64" ? "i64" : "i32";
        var module = request.StructuredDiagnostics
            ? CreateStructuredModule(result, normalizeResult, addressType)
            : CreateStandardModule(result, normalizeResult);
        if (request.Target.Width == "wasm64")
        {
            module = module
                .Replace("cm32p2", "cm64p2", StringComparison.Ordinal);
        }
        _modules.Write(module, request.OutputPath);
    }

    private static string CreateStandardModule(string result, string normalizeResult) =>
        "(module " +
        $"(import \"netwasm.application.v1\" \"run\" (func $application_run {result})) " +
        "(func (export \"cm32p2|wasi:cli/run@0.2|run\") (result i32) " +
        $"call $application_run {normalizeResult}))";

    private static string CreateStructuredModule(
        string result,
        string normalizeResult,
        string addressType) =>
            "(module " +
            $"(import \"netwasm.application.v1\" \"run\" (func $application_run {result})) " +
            "(import \"netwasm.host.v1\" \"terminal_exception\" (tag $terminal)) " +
            "(import \"netwasm.runtime.v1\" \"command_exception_write\" (func $write)) " +
            "(import \"netwasm.runtime.v1\" \"command_exception_completion\" " +
            $"(func $completion (param i32) (result {addressType}))) " +
            "(import \"netwasm.runtime.v1\" \"command_exception_release\" (func $release)) " +
            "(global $entered (mut i32) (i32.const 0)) " +
            "(func $execute (result i32) " +
            "global.get $entered if unreachable end i32.const 1 global.set $entered " +
            "(block $failed (try_table (catch $terminal $failed) " +
            $"call $application_run {normalizeResult} return)) i32.const 2) " +
            "(func (export \"cm32p2|wasi:cli/run@0.2|run\") (result i32) (local $status i32) " +
            "call $execute local.tee $status i32.const 2 i32.eq " +
            "if call $write i32.const 1 return end local.get $status) " +
            "(func (export \"cm32p2|netwasm:diagnostics/command@1|run\") " +
            $"(result {addressType}) call $execute call $completion) " +
            "(func (export \"cm32p2|netwasm:diagnostics/command@1|run_post\") " +
            $"(param {addressType}) call $release))";
}
