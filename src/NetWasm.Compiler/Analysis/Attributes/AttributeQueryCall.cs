// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal enum AttributeQueryApiKind
{
    Instance,
    AttributeStatic,
    Extension,
}

internal sealed record AttributeQueryCall(
    AttributeQueryOperation Operation,
    int ArgumentCount,
    int? FilterArgument,
    CliTypeIdentity? GenericFilter,
    int? InheritArgument)
{
    // Which family of overloads the call is: desktop answers one target and filter differently by family (an interface
    // filter is a target's own query but an ArgumentException for Attribute's statics and the extensions).
    public AttributeQueryApiKind Api { get; init; } = AttributeQueryApiKind.Instance;

    // The filter of a query that names none: object for a target's own GetCustomAttributes(bool), Attribute for
    // Attribute's statics and the extensions.
    public CliTypeIdentity? ImplicitFilter { get; init; }
}
