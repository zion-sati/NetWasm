// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deterministic managed port of dotnet/runtime System.Private.CoreLib AppContext.cs.
// Upstream commit: deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Reflection-based target-framework discovery and process exit/event hooks are intentionally excluded from the
// NetWasm profile. The host properties .NET reads from runtimeconfig.json are the project's
// RuntimeHostConfigurationOption items, which the compiler supplies.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System;

public static partial class AppContext
{
    private static Dictionary<string, object?>? s_dataStore;
    private static Dictionary<string, bool>? s_switches;

    // The project's RuntimeHostConfigurationOption items, as .NET's host gives AppContext runtimeconfig.json's
    // properties: each name and value followed by '\0'. The compiler gives this method its body
    // (RuntimeConfigurationRewriter); read in place, so a program that reads no property keeps no table.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ConfigurationProperties() => string.Empty;

    private static string? ConfiguredValue(string name)
    {
        string properties = ConfigurationProperties();
        int index = 0;
        while (index < properties.Length)
        {
            int nameEnd = properties.IndexOf('\0', index);
            int valueEnd = properties.IndexOf('\0', nameEnd + 1);
            if (properties.AsSpan(index, nameEnd - index).SequenceEqual(name))
            {
                return properties.Substring(nameEnd + 1, valueEnd - nameEnd - 1);
            }
            index = valueEnd + 1;
        }
        return null;
    }

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
        if (dataStore is not null)
        {
            lock (dataStore)
            {
                if (dataStore.TryGetValue(name, out var data))
                {
                    return data;
                }
            }
        }
        return ConfiguredValue(name);
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
