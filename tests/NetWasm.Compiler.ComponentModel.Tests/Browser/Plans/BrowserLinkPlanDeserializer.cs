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
