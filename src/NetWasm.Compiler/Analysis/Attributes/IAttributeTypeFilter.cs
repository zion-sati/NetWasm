// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Whether an attribute of the given type answers a query for the filter type: the attribute type is the filter or
// derives from it, or, for an open generic attribute type (typeof(TagAttribute<>)), the attribute type or one of its
// bases is a constructed instance of it, as .NET's custom attribute filter (MatchesTypeFilter) decides.
internal interface IAttributeTypeFilter
{
    bool Matches(CliTypeIdentity attributeType, CliTypeIdentity filter);
}
