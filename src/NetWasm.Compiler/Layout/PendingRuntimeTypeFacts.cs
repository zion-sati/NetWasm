using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

// A registered type, as the runtime type table describes it.
internal sealed record PendingRuntimeTypeFacts(
    CliTypeIdentity Identity,
    TypeDefinitionModel? Definition,
    int TypeId)
{
    internal ImmutableArray<int> GenericArgumentTypeIds { get; init; } = [];
}
