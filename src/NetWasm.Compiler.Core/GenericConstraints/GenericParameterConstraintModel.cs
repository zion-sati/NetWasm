using System.Collections.Immutable;
using System.Reflection;

namespace NetWasm.Compiler.Core.GenericConstraints;

public sealed record GenericParameterConstraintModel(
    GenericParameterAttributes Attributes,
    ImmutableArray<CliTypeIdentity> Types);
