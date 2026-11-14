// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// From src/coreclr/nativeaot's Debugger.cs, the runtime without a managed debugger. NetWasm adaptation:
// the non-Windows Break path is kept and the native-debugger import is dropped; Log and IsLogging
// report that no debugger logs, as upstream's placeholders do.

using System.Runtime.CompilerServices;

namespace System.Diagnostics
{
    public static partial class Debugger
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        [DebuggerHidden] // this helps VS appear to stop on the source line calling Debugger.Break() instead of inside it
        public static void Break()
        {
        }

        public static bool IsAttached
        {
            get
            {
                // Managed debugger is never attached because we don't have one
                return false;
            }
        }

        // NetWasm: native AOT throws; .NET's contract is "true if a debugger is attached or was started", which
        // CoreCLR on Linux meets by returning false when it cannot start one. There is none to start here.
        public static bool Launch() => IsAttached;

        public static void NotifyOfCrossThreadDependency()
        {
            // nothing to do...yet
        }

        /// <summary>
        /// Posts a message for the attached debugger.  If there is no
        /// debugger attached, has no effect.  The debugger may or may not
        /// report the message depending on its settings.
        /// </summary>
        public static void Log(int level, string category, string message)
        {
        }

        /// <summary>
        /// Checks to see if an attached debugger has logging enabled
        /// </summary>
        public static bool IsLogging()
        {
            return false;
        }
    }
}
