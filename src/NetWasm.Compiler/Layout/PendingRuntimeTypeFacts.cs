using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed record PendingRuntimeTypeFacts(
    CliTypeIdentity Identity,
    TypeDefinitionModel? Definition,
    int TypeId,
    int BaseTypeId,
    int AssignableTypeIdsAddress,
    int AssignableTypeIdCount,
    string? DelegateInvokeDescriptor)
{
    internal ImmutableArray<int> GenericArgumentTypeIds { get; init; } = [];
}
