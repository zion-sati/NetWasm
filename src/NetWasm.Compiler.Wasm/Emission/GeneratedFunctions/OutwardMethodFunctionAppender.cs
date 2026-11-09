using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class OutwardMethodFunctionAppender(
    IAsyncJSExportWrapperEmitter asyncWrappers,
    IAsyncJSExportHelperAppender asyncHelpers,
    IEntryPointEmitter synchronousEntries,
    IManagedMethodFunctionTypeResolver functionTypes,
    IManagedBoundaryPlanBuilder boundaries,
    IRuntimeImportResolver runtimeImports,
    ISynchronousJSExportEmitter? synchronousJSExports = null,
    ISynchronousJSExportFunctionTypeResolver? synchronousJSExportTypes = null) :
    IOutwardMethodFunctionAppender
{
    public int Append(OutwardMethodFunctionAppendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Functions);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ImportCount);
        ArgumentNullException.ThrowIfNull(request.AsyncHelperIndices);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GeneratedFunctionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExportName);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.Initialization);
        ArgumentNullException.ThrowIfNull(request.FunctionIndices);
        ArgumentNullException.ThrowIfNull(request.BoundaryEntries);

        var functionIndex = request.ImportCount + request.Functions.Count;
        ManagedBoundaryKind kind;
        if (request.AsyncBinding is { } binding)
        {
            var names = request.AsyncNames ?? throw new ArgumentException(
                "an asynchronous outward boundary requires helper names",
                nameof(request));
            var kinds = request.AsyncKinds ?? throw new ArgumentException(
                "an asynchronous outward boundary requires boundary kinds",
                nameof(request));
            // A JavaScript boundary passes string and array arguments as host handles.
            var javaScriptArguments = request.InteropImports;
            var completion = new AsyncTaskCompletionPlan(
                request.FunctionIndices.Resolve(binding.GetVoidResult ??
                    throw new InvalidOperationException("An asynchronous boundary requires task fault observation.")),
                runtimeImports.Resolve(
                    RuntimeImportSymbol.ManagedExceptionCapture,
                    request.Initialization.RuntimeImportSelection));
            request.Functions.Add(new WasmFunctionDefinition(
                request.GeneratedFunctionName,
                new(
                    request.ArgumentFactory is null
                        ? [.. request.Method.Signature.ParameterSignatureTypes.SelectMany((type, index) =>
                            javaScriptArguments is null
                                ? [request.Method.Signature.ParameterTypes[index]]
                                : InteropTypeClassifier.TransportKinds(type, request.Method.Signature.ParameterTypes[index]))]
                        : [],
                    CliValueKind.I4),
                asyncWrappers.Emit(
                    request.Method,
                    binding,
                    request.Initialization,
                    request.HasFinalizers,
                    request.FunctionIndices,
                    request.ArgumentFactory,
                    javaScriptArguments)));
            asyncHelpers.Append(
                request.Functions,
                request.ImportCount,
                request.AsyncHelperIndices,
                binding,
                names,
                kinds,
                request.BoundaryEntries,
                completion,
                javaScriptArguments);
            kind = kinds.Start;
        }
        else
        {
            var isJSExport = request.SynchronousKind == ManagedBoundaryKind.SynchronousExport &&
                request.InteropImports is not null &&
                (request.Method.Signature.ParameterSignatureTypes.Any(
                    InteropTypeClassifier.IsConverted) ||
                 InteropTypeClassifier.IsConverted(
                     request.Method.Signature.ReturnSignatureType));
            var type = isJSExport
                ? RequireJSExportTypes().Resolve(request.Method)
                : functionTypes.Resolve(request.Method);
            if (request.EntryCompletion is { } completion)
            {
                type = new(type.Parameters, completion.Signature.ReturnType);
            }
            request.Functions.Add(new WasmFunctionDefinition(
                request.GeneratedFunctionName,
                request.ArgumentFactory is null
                    ? type
                    : new([], type.Result),
                isJSExport
                    ? RequireJSExporter().Emit(
                        request.Method,
                        request.Initialization,
                        request.HasFinalizers,
                        request.FunctionIndices,
                        request.InteropImports!)
                    : synchronousEntries.Emit(
                        request.Method,
                        request.Initialization,
                        request.HasFinalizers,
                        request.FunctionIndices,
                        request.ReportTerminalExceptions,
                        request.ArgumentFactory,
                        request.EntryCompletion,
                        request.LocalTimePreflight,
                        request.ProcessExit,
                        request.InitializesRuntime)));
            kind = request.SynchronousKind;
        }

        request.BoundaryEntries.Add(boundaries.Build(new(
            [.. request.Functions],
            request.ImportCount,
            functionIndex,
            request.ExportName,
            kind,
            true)));
        return functionIndex;

        ISynchronousJSExportEmitter RequireJSExporter() => synchronousJSExports ??
            throw new InvalidOperationException(
                "synchronous JS export marshalling was not configured");
        ISynchronousJSExportFunctionTypeResolver RequireJSExportTypes() =>
            synchronousJSExportTypes ?? throw new InvalidOperationException(
                "synchronous JS export function types were not configured");
    }
}
