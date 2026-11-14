// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Diagnostics
{
    /// <devdoc>
    ///     A set of values indicating how the window should appear when starting
    ///     a process.
    /// </devdoc>
    public enum ProcessWindowStyle
    {
        /// <devdoc>
        ///     Show the window in a default location.
        /// </devdoc>
        Normal,

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        Hidden,

        /// <devdoc>
        ///     Show the window minimized.
        /// </devdoc>
        Minimized,

        /// <devdoc>
        ///     Show the window maximized.
        /// </devdoc>
        Maximized
    }
}
