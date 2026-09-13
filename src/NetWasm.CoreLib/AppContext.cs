// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deterministic managed port of dotnet/runtime System.Private.CoreLib AppContext.cs.
// Upstream commit: 811225a482702af7ecc35d817966bc70b88a3a23.
// Host-property probing, reflection-based target-framework discovery, and process
// exit/event hooks are intentionally excluded from the NetWasm profile.

using System.Collections.Generic;

namespace System;

public static partial class AppContext
{
    private static readonly Dictionary<string, object?> s_dataStore = new();
    private static readonly Dictionary<string, bool> s_switches = new();

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
        lock (s_dataStore)
        {
            return s_dataStore.TryGetValue(name, out var data) ? data : null;
        }
    }

    public static void SetData(string name, object? data)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (s_dataStore)
        {
            s_dataStore[name] = data;
        }
    }

    public static bool TryGetSwitch(string switchName, out bool isEnabled)
    {
        ArgumentException.ThrowIfNullOrEmpty(switchName);
        lock (s_switches)
        {
            if (s_switches.TryGetValue(switchName, out isEnabled))
            {
                return true;
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
        lock (s_switches)
        {
            s_switches[switchName] = isEnabled;
        }
    }
}
