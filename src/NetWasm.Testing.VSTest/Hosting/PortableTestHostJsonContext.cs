// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace NetWasm.Testing.VSTest.Hosting;

[JsonSerializable(typeof(PortableTestHostManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class PortableTestHostJsonContext : JsonSerializerContext;
