using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeNativeCacheEvidence(
    RuntimeLinkedMemoryLayout Layout,
    ImmutableArray<RuntimeValidatedNativeBinding> Bindings);
