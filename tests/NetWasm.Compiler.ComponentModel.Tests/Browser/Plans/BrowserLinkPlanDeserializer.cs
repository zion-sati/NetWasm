// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

// Reads a serialized plan back, so tests check what the serializer writes.
internal static class BrowserLinkPlanDeserializer
{
    public static BrowserLinkPlan Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize(json, BrowserLinkPlanJsonContext.Default.BrowserLinkPlan)
            ?? throw new JsonException("A browser link plan must be an object.");
    }
}
