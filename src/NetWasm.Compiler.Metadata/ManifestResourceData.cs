// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Metadata;

// An embedded manifest resource: its name and bytes, as Assembly.GetManifestResourceStream returns them.
public sealed record ManifestResourceData(string Name, ReadOnlyMemory<byte> Data);
