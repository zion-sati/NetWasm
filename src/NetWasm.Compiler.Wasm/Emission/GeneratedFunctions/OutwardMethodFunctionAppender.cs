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
            var process = kinds.Completion == ManagedBoundaryKind.AsynchronousProcessCompletion;
            int? deliverFunctionIndex = process
                ? request.Initialization.RuntimeImportSelection.IncludeTerminalExceptionReporter
                    ? runtimeImports.Resolve(
                        RuntimeImportSymbol.ManagedTerminalExceptionReport,
                        request.Initialization.RuntimeImportSelection)
                    : null
                : runtimeImports.Resolve(
                    RuntimeImportSymbol.ManagedExceptionCapture,
                    request.Initialization.RuntimeImportSelection);
            var completion = new AsyncTaskCompletionPlan(
                request.FunctionIndices.Resolve(binding.GetVoidResult ??
                    throw new InvalidOperationException("An asynchronous boundary requires task fault observation.")),
                deliverFunctionIndex,
                process ? CliValueKind.Void : CliValueKind.I4);
            request.Functions.Add(new WasmFunctionDefinition(
                request.GeneratedFunctionName,
                new(
                    request.ArgumentFactory is null
                        ? request.Method.Signature.ParameterTypes
                        : [],
                    CliValueKind.I4),
                asyncWrappers.Emit(
                    request.Method,
                    binding,
                    request.Initialization,
                    request.HasFinalizers,
                    request.FunctionIndices,
                    request.ArgumentFactory)));
            asyncHelpers.Append(
                request.Functions,
                request.ImportCount,
                request.AsyncHelperIndices,
                binding,
                names,
                kinds,
                request.BoundaryEntries,
                completion);
            kind = kinds.Start;
        }
        else
        {
            var isJSExport = request.SynchronousKind == ManagedBoundaryKind.SynchronousExport &&
                request.InteropImports is not null &&
                (request.Method.Signature.ParameterSignatureTypes.Any(
                    type => InteropTypeClassifier.IsString(type) ||
                        InteropTypeClassifier.IsByteArray(type)) ||
                 InteropTypeClassifier.IsString(
                     request.Method.Signature.ReturnSignatureType) ||
                 InteropTypeClassifier.IsByteArray(
                     request.Method.Signature.ReturnSignatureType));
            var type = isJSExport
                ? RequireJSExportTypes().Resolve(request.Method)
                : functionTypes.Resolve(request.Method);
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
                        request.ArgumentFactory)));
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
