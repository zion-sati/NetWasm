// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawImportBindingSelection(
    RawWitImportCatalog Catalog,
    ImmutableArray<RawCanonicalImportIdentity> WitImports,
    ImmutableArray<RawCanonicalImportIdentity> JavaScriptImports,
    ImmutableArray<RawCanonicalImportIdentity> RuntimeImports);

public sealed record RawImportBindingSelectionRequest(
    RawWitImportCatalog Catalog,
    ImmutableArray<RawCanonicalImportIdentity> FinalImports,
    ImmutableHashSet<RawCanonicalImportIdentity> JavaScriptImports,
    ImmutableHashSet<RawCanonicalImportIdentity> RuntimeImports);
