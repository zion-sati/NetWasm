// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Catalogs;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed record RawBindingManifest(
    int SchemaVersion,
    string Target,
    ImmutableArray<WitInterfaceFunction> RequiredImports,
    ImmutableArray<string> ImportModules);
