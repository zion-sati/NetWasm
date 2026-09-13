using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class OutwardMethodFunctionAppender(
    IAsyncJSExportWrapperEmitter asyncWrappers,
    IAsyncJSExportHelperAppender asyncHelpers,
    IEntryPointEmitter synchronousEntries,
    IManagedMethodFunctionTypeResolver functionTypes,
    IManagedBoundaryPlanBuilder boundaries) : IOutwardMethodFunctionAppender
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
                request.BoundaryEntries);
            kind = kinds.Start;
        }
        else
        {
            var type = functionTypes.Resolve(request.Method);
            request.Functions.Add(new WasmFunctionDefinition(
                request.GeneratedFunctionName,
                request.ArgumentFactory is null
                    ? type
                    : new([], type.Result),
                synchronousEntries.Emit(
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
    }
}
