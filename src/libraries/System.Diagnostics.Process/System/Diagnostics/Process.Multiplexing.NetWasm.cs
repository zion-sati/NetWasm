// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm platform layer for dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// Process.Multiplexing.cs. Reading both output streams needs a process started with them
// redirected, which NetWasm cannot start, so each reader makes upstream's state checks (a disposed
// process, or one without redirected streams, throws as upstream) and never reaches the pipes.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.Diagnostics
{
    public partial class Process
    {
        public (string StandardOutput, string StandardError) ReadAllText(TimeSpan? timeout = default)
        {
            ValidateReadAllState();
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        public (byte[] StandardOutput, byte[] StandardError) ReadAllBytes(TimeSpan? timeout = default)
        {
            ValidateReadAllState();
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        public IEnumerable<ProcessOutputLine> ReadAllLines(TimeSpan? timeout = default)
        {
            ValidateReadAllState();
            if (timeout.HasValue)
            {
                _ = ProcessUtils.ToTimeoutMilliseconds(timeout.Value);
            }
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        // Upstream's asynchronous readers check the state inside the task they return.
        public Task<(string StandardOutput, string StandardError)> ReadAllTextAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<(string StandardOutput, string StandardError)>(ReadAllUnavailable());

        public Task<(byte[] StandardOutput, byte[] StandardError)> ReadAllBytesAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<(byte[] StandardOutput, byte[] StandardError)>(ReadAllUnavailable());

#pragma warning disable CS1998 // An async iterator, as upstream's: the checks run when enumeration starts.
        public async IAsyncEnumerable<ProcessOutputLine> ReadAllLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore CS1998
        {
            ValidateReadAllState();
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
#pragma warning disable CS0162 // An iterator needs a yield.
            yield break;
#pragma warning restore CS0162
        }

        private Exception ReadAllUnavailable()
        {
            try
            {
                ValidateReadAllState();
            }
            catch (Exception exception)
            {
                return exception;
            }
            return new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        private void ValidateReadAllState()
        {
            CheckDisposed();

            if (_standardOutput is null)
            {
                throw new InvalidOperationException(SR.CantGetStandardOut);
            }
            else if (_standardError is null)
            {
                throw new InvalidOperationException(SR.CantGetStandardError);
            }
            else if (_outputStreamReadMode != StreamReadMode.Undefined)
            {
                throw new InvalidOperationException(SR.CantMixSyncAsyncOperation);
            }
            else if (_errorStreamReadMode != StreamReadMode.Undefined)
            {
                throw new InvalidOperationException(SR.CantMixSyncAsyncOperation);
            }

            _outputStreamReadMode = StreamReadMode.SyncMode;
            _errorStreamReadMode = StreamReadMode.SyncMode;
        }
    }
}
