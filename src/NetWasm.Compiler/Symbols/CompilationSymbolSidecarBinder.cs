using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Symbols;

public interface ICompilationSymbolSidecarBinder
{
    CompilationResult Bind(
        CompilationResult result,
        RuntimeTypeSymbolTable types,
        string buildId,
        ImmutableArray<WasmStackTraceSymbol> methodSymbols,
        CompilerOptions options);
}

// A compile's symbols sidecars: type-symbols always, naming the exception types a host's crash report names,
// or every type with NetWasmTypeNames; method-symbols with NetWasmManagedStackTrace.
public sealed class CompilationSymbolSidecarBinder(ISymbolSidecarWriter writer) : ICompilationSymbolSidecarBinder
{
    private readonly ISymbolSidecarWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public CompilationResult Bind(
        CompilationResult result,
        RuntimeTypeSymbolTable types,
        string buildId,
        ImmutableArray<WasmStackTraceSymbol> methodSymbols,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(options);
        var named = options.EmitTypeNames
            ? types.Symbols
            : types.Symbols.Where(symbol => types.ExceptionTypeIds.Contains(symbol.TypeId)).ToImmutableArray();
        return result with
        {
            TypeSymbols = _writer.Write(SymbolSidecarKind.Type, buildId, [.. named.Select(symbol =>
                new SymbolSidecarEntry(symbol.TypeId,
                    [symbol.Name, symbol.Namespace, symbol.FullName, symbol.DisplayName]))]),
            MethodSymbols = options.EmitStackTrace
                ? _writer.Write(SymbolSidecarKind.Method, buildId, [.. (methodSymbols.IsDefault ? [] : methodSymbols)
                    .Select(symbol => new SymbolSidecarEntry(symbol.Id, [symbol.Name]))])
                : null,
        };
    }
}
