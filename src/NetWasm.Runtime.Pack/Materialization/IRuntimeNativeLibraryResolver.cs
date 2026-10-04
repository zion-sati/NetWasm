using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeLibraryResolver
{
    ImmutableArray<RuntimeNativeLibrary> Resolve(RuntimeNativeLibraryResolutionRequest request);
}

internal sealed record RuntimeNativeLibraryResolutionRequest(
    string Target,
    ImmutableArray<RuntimeNativeImport> Imports,
    ImmutableArray<RuntimeNativeLibraryDescriptor> Providers);
