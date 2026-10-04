using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeProviderSelector
{
    ImmutableArray<RuntimeNativeBinding> Select(RuntimeNativeProviderSelectionRequest request);
}

internal sealed record RuntimeNativeProviderSelectionRequest(
    string Target,
    ImmutableArray<RuntimeNativeImport> Imports,
    ImmutableArray<RuntimeNativeLibrary> Providers);
