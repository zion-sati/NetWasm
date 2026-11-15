// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ExternalToolInvocation(
    string Executable,
    ImmutableArray<string> Arguments,
    ImmutableArray<string> EnvironmentVariablePrefixesToRemove);
