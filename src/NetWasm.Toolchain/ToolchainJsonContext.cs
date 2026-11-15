// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain;

// The toolchain's JSON contracts. Each reader passes its own options.
[JsonSerializable(typeof(PinnedToolchainManifest))]
[JsonSerializable(typeof(JcoClosureIntegrityManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class ToolchainJsonContext : JsonSerializerContext;
