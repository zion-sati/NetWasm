// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics;

namespace System.Collections.Generic
{
    internal sealed class StackDebugView<T>
    {
        private readonly Stack<T> _stack;

        public StackDebugView(Stack<T> stack)
        {
            ArgumentNullException.ThrowIfNull(stack);

            _stack = stack;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items
        {
            get
            {
                return _stack.ToArray();
            }
        }
    }
}
