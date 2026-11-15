// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace NetWasm.TimeZones;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\n")]
[JsonSerializable(typeof(TimeZoneDeploymentManifest))]
[JsonSerializable(typeof(string))]
[ExcludeFromCodeCoverage]
internal sealed partial class TimeZoneJsonContext : JsonSerializerContext;
