// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Current-domain identity and AppContext data are managed. Native domain isolation
// is unavailable; reflection/loading/event contracts are reviewed separately.
namespace System;

public sealed partial class AppDomain : MarshalByRefObject
{
    private static AppDomain? s_domain;
    private AppDomain() { }
    public static AppDomain CurrentDomain => s_domain ??= new AppDomain();
    public int Id => 1;
    public bool IsDefaultAppDomain() => true;
    public bool IsFinalizingForUnload() => false;
    public bool IsFullyTrusted => true;
    public bool IsHomogenous => true;
    public string BaseDirectory => AppContext.BaseDirectory;
    public string? RelativeSearchPath => null;
    public string? DynamicDirectory => null;
    // Upstream fallback when entry-assembly reflection is unavailable.
    public string FriendlyName => "DefaultDomain";
    public bool ShadowCopyFiles => false;
    public object? GetData(string name) => AppContext.GetData(name);
    public void SetData(string name, object? data) => AppContext.SetData(name, data);
    public bool? IsCompatibilitySwitchSet(string value) =>
        AppContext.TryGetSwitch(value, out var result) ? result : default(bool?);
    public string ApplyPolicy(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);
        if (assemblyName[0] == '\0') throw new ArgumentException("The value cannot be an empty string.", nameof(assemblyName));
        return assemblyName;
    }
    public override string ToString() => "Name:" + FriendlyName + Environment.NewLine + "There are no context policies.";
    public static int GetCurrentThreadId() => Environment.CurrentManagedThreadId;
    public static AppDomain CreateDomain(string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(friendlyName);
        throw new PlatformNotSupportedException("AppDomain creation is not supported on this NetWasm platform.");
    }
    public static void Unload(AppDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        throw new PlatformNotSupportedException("AppDomain unloading is not supported on this NetWasm platform.");
    }
}
