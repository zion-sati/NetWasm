using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Catalogs;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed record RawBindingManifest(
    int SchemaVersion,
    string Target,
    ImmutableArray<WitInterfaceFunction> RequiredImports);
