// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.Hosting.Execution;

internal sealed record ArtifactProcessStart(
    string FileName,
    ImmutableArray<string> Arguments,
    string WorkingDirectory);
