// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

// Every assembly with a resource the program keeps, and those resources in metadata order.
internal interface IProgramManifestResources
{
    ImmutableDictionary<AssemblyIdentity, ImmutableArray<ManifestResourceData>> KeptResources();
}
