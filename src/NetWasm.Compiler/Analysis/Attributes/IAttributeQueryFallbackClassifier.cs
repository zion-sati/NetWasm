// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryFallbackClassifier
{
    // Why the reference keeps the query's run-time path, which throws PlatformNotSupportedException; null where it never
    // runs that path (code nothing reaches) or throws what desktop throws first (a null target or filter, a filter that is
    // not an attribute type for Attribute's statics and the extensions).
    AttributeQueryFallback? Classify(AttributeQueryReference reference, IEnumerable<CliTypeIdentity> runtimeTypes);
}
