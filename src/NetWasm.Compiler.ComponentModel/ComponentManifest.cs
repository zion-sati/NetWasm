using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ComponentManifest(
    int Version,
    string Package,
    string World,
    string Target,
    string WasiVersion,
    string CanonicalStringEncoding,
    string NormalizedWitSha256,
    string WasmToolsVersion,
    ImmutableArray<string> ImportedInterfaces,
    ImmutableArray<string> ExportedInterfaces,
    ImmutableArray<string> ImportedFunctions,
    ImmutableArray<string> ExportedFunctions)
{
    public ComponentJavaScriptBoundary JavaScript { get; init; } =
        ComponentJavaScriptBoundary.Empty;

    public ComponentAdapterVersions Adapters { get; init; } =
        ComponentAdapterVersions.None;
}

public sealed record ComponentJavaScriptBoundary(
    ImmutableArray<ComponentJavaScriptImport> Imports,
    ImmutableArray<ComponentJavaScriptExport> Exports)
{
    public static ComponentJavaScriptBoundary Empty { get; } = new([], []);
}

public sealed record ComponentJavaScriptImport(
    string Module,
    string Name,
    ImmutableArray<string> Parameters,
    string Result,
    string? AsyncReturn);

public sealed record ComponentJavaScriptExport(
    string Name,
    ImmutableArray<string> Parameters,
    string Result,
    string? AsyncReturn);

public sealed record ComponentAdapterVersions(
    string? Jco,
    string? Preview2Shim)
{
    public static ComponentAdapterVersions None { get; } = new(null, null);
}

public sealed record ComponentManifestInputs(
    ComponentJavaScriptBoundary JavaScript,
    ComponentAdapterVersions Adapters)
{
    public ImmutableArray<HostInteropWitImport> WitImports { get; init; } = [];
}
