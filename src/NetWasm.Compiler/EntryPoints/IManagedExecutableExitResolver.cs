using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface IManagedExecutableExitResolver
{
    // CoreLib's process-exit method, which a managed executable's entry calls
    // after Main returns, or null for any other entry.
    EntityKey? Resolve(CompilerEntryPointKind kind, ITypeFinder types, IMethodRepository methods);
}
