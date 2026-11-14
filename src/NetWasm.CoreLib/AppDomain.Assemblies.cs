// Closed-world host boundary for assembly loading and notification contracts.
using System.Reflection;
namespace System;

public sealed partial class AppDomain
{
    public Assembly Load(AssemblyName assemblyRef) => throw new PlatformNotSupportedException("Runtime assembly loading is not supported on NetWasm.");
    public int ExecuteAssemblyByName(AssemblyName assemblyName, params string?[]? args) => throw new PlatformNotSupportedException("Executing additional assemblies is not supported on NetWasm.");
    public Assembly Load(byte[] rawAssembly) => throw new PlatformNotSupportedException("Runtime assembly loading and enumeration are not supported on NetWasm.");
    public Assembly Load(byte[] rawAssembly, byte[]? rawSymbolStore) => throw new PlatformNotSupportedException("Runtime assembly loading and enumeration are not supported on NetWasm.");
    public Assembly Load(string assemblyString) => throw new PlatformNotSupportedException("Runtime assembly loading and enumeration are not supported on NetWasm.");
    public Assembly[] GetAssemblies() => throw new PlatformNotSupportedException("Runtime assembly loading and enumeration are not supported on NetWasm.");
    public Assembly[] ReflectionOnlyGetAssemblies() => throw new PlatformNotSupportedException("Runtime assembly loading and enumeration are not supported on NetWasm.");
    public event AssemblyLoadEventHandler? AssemblyLoad
    {
        add => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
        remove => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
    }
    public event ResolveEventHandler? AssemblyResolve
    {
        add => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
        remove => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
    }
    public event ResolveEventHandler? ReflectionOnlyAssemblyResolve
    {
        add => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
        remove => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
    }
    public event ResolveEventHandler? ResourceResolve
    {
        add => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
        remove => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
    }
    public event ResolveEventHandler? TypeResolve
    {
        add => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
        remove => throw new PlatformNotSupportedException("AppDomain assembly notifications are not supported on NetWasm.");
    }
}
