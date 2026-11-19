// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// How a missing member's types read in its MissingMethodException or MissingFieldException message: by their full
// names where they have them.
internal static class MissingMemberDisplay
{
    public static string Type(CliTypeIdentity type) => type.FullName ?? type.CanonicalName;
}
