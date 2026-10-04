using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed record UnsafeAccessorGenericConstraints(
    ImmutableArray<UnsafeAccessorGenericParameterConstraints> TypeParameters,
    ImmutableArray<UnsafeAccessorGenericParameterConstraints> MethodParameters);

internal sealed record UnsafeAccessorGenericParameterConstraints(
    GenericParameterAttributes Attributes,
    ImmutableArray<CliTypeIdentity> Types);
