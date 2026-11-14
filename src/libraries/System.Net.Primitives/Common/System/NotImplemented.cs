// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System
{
    //
    // Support for tooling-friendly NotImplementedExceptions.
    //
    internal static class NotImplemented
    {
        /// <summary>
        /// Permanent NotImplementedException with no message shown to user.
        /// </summary>
        internal static Exception ByDesign => new NotImplementedException();

        /// <summary>
        /// Permanent NotImplementedException with localized message shown to user.
        /// </summary>
        internal static Exception ByDesignWithMessage(string message)
        {
            return new NotImplementedException(message);
        }
    }
}
