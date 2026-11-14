// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (NamedPipeClientStream.Unix.cs). NetWasm has no named pipes: connecting and the server instance
// count throw PlatformNotSupportedException, as does the pipe path that the constructors compute.
// The PipeAccessRights constructor is not ported because PipeAccessRights is not.

namespace System.IO.Pipes
{
    public sealed partial class NamedPipeClientStream
    {
        private static int AccessRightsFromDirection(PipeDirection _) => 0;

        private bool TryConnect(int _ /* timeout */) =>
            throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);

        public int NumberOfServerInstances =>
            throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);
    }
}
