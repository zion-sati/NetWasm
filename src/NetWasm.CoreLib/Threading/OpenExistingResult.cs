// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Threading
{
    /// <summary>
    /// Mapping of the different success or failure values that can occur when p/invoking Interop.Kernel32.OpenEvent,
    /// which is called inside the OpenExistingWorker methods defined in EventWaitHandle, Mutex and Semaphore.
    /// </summary>
    internal enum OpenExistingResult
    {
        Success,
        NameNotFound,
        PathNotFound,
        NameInvalid,
        ObjectIncompatibleWithCurrentUserOnly
    }
}
