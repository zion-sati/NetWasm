using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed record UnsafeAccessorMemberRequest(
    UnsafeAccessorMemberKind Kind,
    string Name,
    MethodDefinitionModel Accessor,
    MethodSignature<CliTypeIdentity> Signature,
    TypeDefinitionModel Owner);

internal abstract record UnsafeAccessorMemberMatch
{
    private UnsafeAccessorMemberMatch()
    {
    }

    internal sealed record Method(MethodDefinitionModel Definition) : UnsafeAccessorMemberMatch;
    internal sealed record Field(FieldDefinitionModel Definition) : UnsafeAccessorMemberMatch;
    internal sealed record Failure(UnsafeAccessorFailure Kind) : UnsafeAccessorMemberMatch;
}

internal enum UnsafeAccessorFailure
{
    BadImageFormat,
    MissingMethod,
    MissingField,
    AmbiguousMatch,
    InvalidProgram,
}
