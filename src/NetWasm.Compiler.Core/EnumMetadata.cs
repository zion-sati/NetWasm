using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

/// <summary>
/// A literal enum member read directly from the ECMA-335 metadata tables.
/// RawValue contains the underlying bits, truncated to the underlying type's
/// width; it is deliberately not a managed value or a reflection object.
/// </summary>
public readonly record struct EnumMemberModel(string Name, ulong RawValue);

/// <summary>
/// The compact metadata published for one reachable enum type.
/// </summary>
public readonly record struct EnumMetadataLayout(
    EntityKey Type,
    int TypeId,
    int Address,
    CliTypeIdentity UnderlyingType,
    bool IsFlags,
    ImmutableArray<EnumMetadataMemberLayout> Members);

public readonly record struct EnumMetadataMemberLayout(
    string Name,
    ulong RawValue,
    StringLayout NameLayout);

public interface IEnumMetadataSource
{
    ImmutableArray<EnumMetadataLayout> EnumMetadata { get; }
}
