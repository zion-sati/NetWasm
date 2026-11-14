// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Upstream contracts adapted for NetWasm's closed-world execution model.
namespace System;

public sealed partial class AppDomain
{
    public int ExecuteAssembly(string assemblyFile) => ExecuteAssembly(assemblyFile, null);

    public int ExecuteAssembly(string assemblyFile, string?[]? args)
    {
        ArgumentNullException.ThrowIfNull(assemblyFile);
        throw new PlatformNotSupportedException("Executing additional assemblies is not supported on NetWasm.");
    }

    public int ExecuteAssembly(string assemblyFile, string?[]? args, byte[]? hashValue,
        Configuration.Assemblies.AssemblyHashAlgorithm hashAlgorithm) =>
        throw new PlatformNotSupportedException("Executing assemblies with code-access-security policy is not supported on NetWasm.");

    public int ExecuteAssemblyByName(string assemblyName) => ExecuteAssemblyByName(assemblyName, null);

    public int ExecuteAssemblyByName(string assemblyName, params string?[]? args)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        throw new PlatformNotSupportedException("Executing additional assemblies is not supported on NetWasm.");
    }

    // Unlike upstream's legacy no-ops, reject unsupported configuration explicitly.
    // A successful call must not imply that assembly lookup behavior changed.
    // Upstream's: deprecated, and accepted as no-ops.
    public void AppendPrivatePath(string? path) { }
    public void ClearPrivatePath() { }
    public void ClearShadowCopyPath() { }
    public void SetCachePath(string? path) { }
    public void SetDynamicBase(string? path) { }
    public void SetShadowCopyFiles() { }
    public void SetShadowCopyPath(string? path) { }
}
