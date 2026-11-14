// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Diagnostics.Tracing
{
    /// <summary>
    /// EventActivityOptions flags allow to specify different activity related characteristics.
    /// </summary>
    [Flags]
    public enum EventActivityOptions
    {
        /// <summary>
        /// No special options are added to the event.
        /// </summary>
        None = 0,

        /// <summary>
        /// Disable Implicit Activity Tracking
        /// </summary>
        Disable = 0x2,

        /// <summary>
        /// Allow activity event to call itself (directly or indirectly)
        /// </summary>
        Recursive = 0x4,

        /// <summary>
        /// Allows event activity to live beyond its parent.
        /// </summary>
        Detachable = 0x8
    }
}
