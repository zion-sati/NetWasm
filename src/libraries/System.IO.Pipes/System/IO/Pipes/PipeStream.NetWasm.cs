// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce (PipeStream.Unix.cs).
// NetWasm has no host pipe transport. It keeps the Unix partial's handle-optional validation
// (CheckOperationsRequiresSetHandle) and its no-op DisposeCore; reading, writing, asynchronous and
// APM transfers, buffer-size queries, transmission and read modes, drain, pipe paths and handle
// validation throw PlatformNotSupportedException, so a supplied integer handle never becomes a pipe.
// Subclasses that override Read and Write keep working through ordinary virtual dispatch.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace System.IO.Pipes
{
    public abstract partial class PipeStream : Stream
    {
        // As on Unix, a PipeStream may exist before it has a handle.
        internal const bool CheckOperationsRequiresSetHandle = false;

        public override int Read(byte[] buffer, int offset, int count) => throw PipesNotSupported();

        public override int Read(Span<byte> buffer) => throw PipesNotSupported();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw PipesNotSupported();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken)) =>
            throw PipesNotSupported();

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) =>
            throw PipesNotSupported();

        public override int EndRead(IAsyncResult asyncResult) => throw PipesNotSupported();

        public override void Write(byte[] buffer, int offset, int count) => throw PipesNotSupported();

        public override void Write(ReadOnlySpan<byte> buffer) => throw PipesNotSupported();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw PipesNotSupported();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken)) =>
            throw PipesNotSupported();

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) =>
            throw PipesNotSupported();

        public override void EndWrite(IAsyncResult asyncResult) => throw PipesNotSupported();

        internal static string GetPipePath(string serverName, string pipeName) => throw PipesNotSupported();

#pragma warning disable CA1822
        /// <summary>Throws an exception if the supplied handle does not represent a valid pipe.</summary>
        /// <param name="safePipeHandle">The handle to validate.</param>
        internal void ValidateHandleIsPipe(SafePipeHandle safePipeHandle) => throw PipesNotSupported();
#pragma warning restore CA1822

        /// <summary>Initializes the handle to be used asynchronously.</summary>
        /// <param name="handle">The handle.</param>
        partial void InitializeAsyncHandle(SafePipeHandle handle);

        internal virtual void DisposeCore(bool disposing)
        {
            // nop
        }

        [SupportedOSPlatform("windows")]
        public void WaitForPipeDrain() => throw PipesNotSupported();

        public virtual PipeTransmissionMode TransmissionMode => throw PipesNotSupported();

        public virtual int InBufferSize => throw PipesNotSupported();

        public virtual int OutBufferSize => throw PipesNotSupported();

        public virtual PipeTransmissionMode ReadMode
        {
            get => throw PipesNotSupported();
            set => throw PipesNotSupported();
        }

        private static PlatformNotSupportedException PipesNotSupported() => new PlatformNotSupportedException(SR.Pipes_PlatformNotSupported);

        [DoesNotReturn]
        private static void ThrowPipesNotSupported() => throw PipesNotSupported();
    }
}
