using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed record PendingEnumMetadata(
    EntityKey Type,
    int TypeId,
    CliTypeIdentity UnderlyingType,
    bool IsFlags,
    ImmutableArray<EnumMemberModel> Members);
