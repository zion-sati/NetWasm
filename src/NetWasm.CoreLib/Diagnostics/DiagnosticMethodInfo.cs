// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Diagnostics;

// NetWasm intentionally omits synchronous runtime member metadata. This keeps
// the portable diagnostic contract while reporting that method data is absent.
public sealed class DiagnosticMethodInfo
{
    private DiagnosticMethodInfo()
    {
    }

    public string Name => throw CreateMetadataException();

    public string? DeclaringTypeName => throw CreateMetadataException();

    public string? DeclaringAssemblyName => throw CreateMetadataException();

    public static DiagnosticMethodInfo? Create(Delegate @delegate)
    {
        ArgumentNullException.ThrowIfNull(@delegate);
        return null;
    }

    private static PlatformNotSupportedException CreateMetadataException() =>
        new("Runtime method metadata is not available on NetWasm.");
}
