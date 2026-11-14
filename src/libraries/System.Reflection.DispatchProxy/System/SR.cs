// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Reflection.DispatchProxy uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Reflection.DispatchProxy's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string DispatchProxy_RuntimeProxyNotSupported = "Creating a DispatchProxy instance generates a proxy type at run time, which is not supported on NetWasm.";
    }
}
