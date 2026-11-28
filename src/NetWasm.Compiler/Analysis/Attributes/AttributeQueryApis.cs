// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// The public attribute queries the compiler lowers (AttributeQueryClassifier): IsDefined, GetCustomAttribute and
// GetCustomAttributes on these CoreLib types. The lowering reads a query's target and filter at its call, so the call
// must reach it: the inliner keeps it, as it keeps a reachability marker. Inlined, a query on a Type known only at run
// time became a call to RuntimeCustomAttributes, which nothing lowers, and threw PlatformNotSupportedException. And a
// query returns, whatever its fallback body does (NonReturningCallRewriter): one whose body only throws would otherwise
// lose the code after it before the lowering puts its answer there.
internal static class AttributeQueryApis
{
    internal static readonly ImmutableHashSet<string> OwnerTypes =
    [
        "System.Type",
        "System.Reflection.MemberInfo",
        "System.Reflection.ICustomAttributeProvider",
        "System.Attribute",
        "System.Reflection.CustomAttributeExtensions",
        "System.Reflection.Assembly",
        "System.Reflection.Module",
    ];

    private static readonly ImmutableHashSet<string> Operations = ["IsDefined", "GetCustomAttribute", "GetCustomAttributes"];

    // Whether the method is one of the public queries the lowering reads.
    internal static bool IsQuery(MethodInstanceModel method) =>
        method.DeclaringType.FullName is { } owner && OwnerTypes.Contains(owner) &&
        Operations.Contains(method.Definition.Name);
}
