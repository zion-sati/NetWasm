// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics.CodeAnalysis;

namespace System.Reflection.Metadata
{
    // As Native AOT's: compiled code takes no metadata or IL deltas.
    public static class MetadataUpdater
    {
        public static void ApplyUpdate(Assembly assembly, ReadOnlySpan<byte> metadataDelta, ReadOnlySpan<byte> ilDelta, ReadOnlySpan<byte> pdbDelta)
        {
            throw new PlatformNotSupportedException();
        }

        [FeatureSwitchDefinition("System.Reflection.Metadata.MetadataUpdater.IsSupported")]
        public static bool IsSupported => false;
    }
}
