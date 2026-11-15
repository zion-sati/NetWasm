// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BrowserLinkPlan))]
[JsonSerializable(typeof(BrowserLinkExecutionPlan))]
[ExcludeFromCodeCoverage]
internal sealed partial class BrowserLinkPlanJsonContext : JsonSerializerContext;
