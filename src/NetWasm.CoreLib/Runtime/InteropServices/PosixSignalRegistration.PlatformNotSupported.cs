// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm compiles the variant upstream uses for browser and WASI: registering a handler throws.

using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.InteropServices
{
    public sealed partial class PosixSignalRegistration
    {
        private PosixSignalRegistration() { }

        [DynamicDependency("#ctor")] // Prevent the private ctor and the IDisposable implementation from getting linked away
        private static PosixSignalRegistration Register(PosixSignal signal, Action<PosixSignalContext> handler) =>
            throw new PlatformNotSupportedException();

        partial void Unregister();
    }
}
