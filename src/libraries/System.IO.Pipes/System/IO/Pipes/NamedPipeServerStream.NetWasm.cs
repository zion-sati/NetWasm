// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (NamedPipeServerStream.Unix.cs). NetWasm has no named pipes: creating the server, waiting for and
// ending connections, impersonation and RunAsClient throw PlatformNotSupportedException.

using System.Threading;
using System.Threading.Tasks;

namespace System.IO.Pipes
{
    public sealed partial class NamedPipeServerStream
    {
        private void Create(string pipeName, PipeDirection direction, int maxNumberOfServerInstances,
                PipeTransmissionMode transmissionMode, PipeOptions options, int inBufferSize, int outBufferSize,
                HandleInheritability inheritability) =>
            throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);

        public void WaitForConnection() => throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);

        public Task WaitForConnectionAsync(CancellationToken cancellationToken) =>
            throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);

        public void Disconnect() => throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);

        public string GetImpersonationUserName() => throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);

        public void RunAsClient(PipeStreamImpersonationWorker impersonationWorker) =>
            throw new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);
    }
}
