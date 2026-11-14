using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.NativeInterop;

// Which native imports a program can bind: those of a library it links (a NativeLibrary item's NetWasmLibraryName,
// matched exactly, as native materialization selects providers), plus imports with Wasm import linkage, which the
// host supplies. An import of any other library compiles to a body that throws DllNotFoundException when called, as
// Mono's WebAssembly build fails a call into a module it does not link and .NET on Linux fails a P/Invoke only when
// it runs. With no linked libraries given, every import binds.
public sealed class NativeLibraryBinding : INativeImportBinder
{
    private readonly ImmutableHashSet<string>? _linked;

    public NativeLibraryBinding(IEnumerable<string>? linkedLibraries)
    {
        _linked = linkedLibraries?.ToImmutableHashSet(StringComparer.Ordinal);
    }

    public static NativeLibraryBinding All { get; } = new(null);

    public bool Binds(NativeImportDeclaration import)
    {
        ArgumentNullException.ThrowIfNull(import);
        return _linked is null || import.HasWasmImportLinkage || _linked.Contains(import.LibraryName);
    }
}
