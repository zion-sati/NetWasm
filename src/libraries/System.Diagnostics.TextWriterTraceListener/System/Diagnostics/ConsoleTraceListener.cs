// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Diagnostics
{
    public class ConsoleTraceListener : TextWriterTraceListener
    {
        public ConsoleTraceListener()
            : base(Console.Out)
        {
        }

        public ConsoleTraceListener(bool useErrorStream)
            : base(useErrorStream ? Console.Error : Console.Out)
        {
        }

        // Nop since there are no resources to clean up.
        public override void Close() { }
    }
}
