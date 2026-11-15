// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface ILinkerCommandFactory
{
    RuntimeCommand Create(string linkerPath, ImmutableArray<string> arguments, string logPath);
}
