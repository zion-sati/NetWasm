// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (AnonymousPipeServerStream.Unix.cs). NetWasm cannot create a host pipe, so Create throws
// PlatformNotSupportedException after the common constructor's argument validation.

namespace System.IO.Pipes
{
    public sealed partial class AnonymousPipeServerStream
    {
        private void Create(PipeDirection direction, HandleInheritability inheritability, int bufferSize) =>
            throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);
    }
}
