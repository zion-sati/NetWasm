// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm platform half of dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce's FileSystemWatcher.
// WASI 0.3 has no change-notification interface, so starting to watch throws the exception upstream's
// generated assembly throws on platforms without a watcher (SR.FileSystemWatcher_PlatformNotSupported).
// Everything else is upstream's: path, filter and enum validation, the events and their On* raisers, and
// ISupportInitialize, which defers the start (and so the exception) to EndInit, as on Linux.

namespace System.IO
{
    public partial class FileSystemWatcher
    {
        /// <summary>Starts a new watch operation if one is not currently running.</summary>
        private void StartRaisingEvents()
        {
            // If we're called when "Initializing" is true, set enabled to true
            if (IsSuspended())
            {
                _enabled = true;
                return;
            }

            throw new PlatformNotSupportedException(SR.FileSystemWatcher_PlatformNotSupported);
        }

        /// <summary>Cancels the currently running watch operation if there is one.</summary>
        private void StopRaisingEvents()
        {
            _enabled = false;
        }

        /// <summary>Called when FileSystemWatcher is finalized.</summary>
        private void FinalizeDispose()
        {
        }

        private static void RestartForInternalBufferSize()
        {
            // Nothing is ever started, so there is nothing to restart.
        }
    }
}
