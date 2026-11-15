// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Archives;

public sealed record PackageArchive(
    PackageIdentity Identity,
    ImmutableArray<ArchiveEntry> Entries,
    DeterminismPolicy Policy);
