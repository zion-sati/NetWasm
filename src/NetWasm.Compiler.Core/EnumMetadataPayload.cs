using System;

namespace NetWasm.Compiler.Core;

[Flags]
public enum EnumMetadataPayload
{
    None = 0,
    Values = 1,
    Names = 2,
    RuntimeDescriptor = 4,
}

/// <summary>
/// A null type is a conservative request for every possible enum identity.
/// Otherwise the request applies only to the exact closed metadata identity.
/// </summary>
public readonly record struct EnumMetadataRequirement(
    EntityKey? Type,
    EnumMetadataPayload Payload);
