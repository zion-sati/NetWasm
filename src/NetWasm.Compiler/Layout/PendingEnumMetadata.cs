using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed record PendingEnumMetadata(
    EntityKey Type,
    CliTypeIdentity EnumType,
    int TypeId,
    CliTypeIdentity UnderlyingType,
    bool IsFlags,
    bool IsOpenDefinition,
    ImmutableArray<EnumMemberModel> Members,
    EnumMetadataPayload Payload);
