// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Resources
{
    /// <summary>
    /// Specifies whether a <see cref="ResourceManager" /> object looks for the resources of the app's default culture in the main assembly or in a satellite assembly.
    /// </summary>
    public enum UltimateResourceFallbackLocation
    {
        MainAssembly,
        Satellite
    }
}
