// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Execution;

/// <summary>One explicitly supplied case-sensitive guest environment value.</summary>
public sealed record NetWasmEnvironmentVariable(string Name, string Value);
