using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed class AsynchronousManagedExecutableComponentAdapterWriter(
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
        if (request.EntryPoint.CompletionShape != ManagedExecutableCompletionShape.Asynchronous)
        {
            throw new ArgumentException("The process adapter requires asynchronous completion.", nameof(request));
        }
        var target = request.Target.Width switch
        {
            "wasm32" => WasmTarget.Wasm32,
            "wasm64" => WasmTarget.Wasm64,
            _ => throw new ArgumentOutOfRangeException(nameof(request),
                request.Target.Width, "Unsupported component target width."),
        };
        _ = request.EntryPoint.ParameterShape switch
        {
            ManagedExecutableParameterShape.None => true,
            ManagedExecutableParameterShape.StringArray => true,
            _ => throw new ArgumentOutOfRangeException(nameof(request),
                request.EntryPoint.ParameterShape, "Unsupported managed parameter shape."),
        };
        var (resultImport, readResult) = request.EntryPoint.ReturnShape switch
        {
            ManagedExecutableReturnShape.Void => (string.Empty, "i32.const 0"),
            ManagedExecutableReturnShape.ExitCode =>
                ("(import \"netwasm.application.v1\" \"netwasm.process.result\" " +
                 "(func $process_result (param i32) (result i32))) ",
                 "local.get 0 call $process_result"),
            _ => throw new ArgumentOutOfRangeException(nameof(request),
                request.EntryPoint.ReturnShape, "Unsupported managed return shape."),
        };
        string Export(string operation) => CanonicalAbiNames.Export(
            "netwasm:runtime@1.0.0/process", operation, target);
        _modules.Write(
            "(module " +
            "(import \"netwasm.application.v1\" \"run\" (func $process_start (result i32))) " +
            "(import \"netwasm.application.v1\" \"netwasm.process.status\" (func $process_status (param i32) (result i32))) " +
            resultImport +
            "(import \"netwasm.application.v1\" \"netwasm.process.complete\" (func $process_complete (param i32))) " +
            $"(func (export \"{Export("start")}\") (result i32) call $process_start) " +
            $"(func (export \"{Export("status")}\") (param i32) (result i32) local.get 0 call $process_status) " +
            $"(func (export \"{Export("exit-code")}\") (param i32) (result i32) {readResult}) " +
            $"(func (export \"{Export("complete")}\") (param i32) local.get 0 call $process_complete))",
            request.OutputPath);
    }
}
