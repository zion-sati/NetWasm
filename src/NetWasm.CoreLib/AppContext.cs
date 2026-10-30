// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deterministic managed port of dotnet/runtime System.Private.CoreLib AppContext.cs.
// Upstream commit: 474ed963699fc29809015392738a15231aaae2de.
// Host-property probing, reflection-based target-framework discovery, and process
// exit/event hooks are intentionally excluded from the NetWasm profile.

using System.Collections.Generic;

namespace System;

public static partial class AppContext
{
    private static Dictionary<string, object?>? s_dataStore;
    private static Dictionary<string, bool>? s_switches;

    public static string BaseDirectory
    {
        get => GetData("APP_CONTEXT_BASE_DIRECTORY") as string ?? string.Empty;
    }

    // TargetFrameworkName normally comes from the entry assembly's metadata. That
    // reflection/host boundary is unavailable, so the portable value is null.
    public static string? TargetFrameworkName
    {
        get => null;
    }

    public static object? GetData(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Dictionary<string, object?>? dataStore = s_dataStore;
        if (dataStore is null)
        {
            return null;
        }

        lock (dataStore)
        {
            return dataStore.TryGetValue(name, out var data) ? data : null;
        }
    }

    public static void SetData(string name, object? data)
    {
        ArgumentNullException.ThrowIfNull(name);

        // TODO(netwasm-threading)
        Dictionary<string, object?> dataStore = s_dataStore ??= new();
        lock (dataStore)
        {
            dataStore[name] = data;
        }
    }

    public static bool TryGetSwitch(string switchName, out bool isEnabled)
    {
        ArgumentException.ThrowIfNullOrEmpty(switchName);

        Dictionary<string, bool>? switches = s_switches;
        if (switches is not null)
        {
            lock (switches)
            {
                if (switches.TryGetValue(switchName, out isEnabled))
                {
                    return true;
                }
            }
        }

        if (GetData(switchName) is string value && bool.TryParse(value, out isEnabled))
        {
            return true;
        }

        isEnabled = false;
        return false;
    }

    public static void SetSwitch(string switchName, bool isEnabled)
    {
        ArgumentException.ThrowIfNullOrEmpty(switchName);

        // TODO(netwasm-threading)
        Dictionary<string, bool> switches = s_switches ??= new();
        lock (switches)
        {
            switches[switchName] = isEnabled;
        }
    }
}
