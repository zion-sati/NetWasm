// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading
{
    /// <summary>Describes a value change observed while an execution context changes.</summary>
    public readonly struct AsyncLocalValueChangedArgs<T>
    {
        public T? PreviousValue { get; }
        public T? CurrentValue { get; }
        public bool ThreadContextChanged { get; }

        internal AsyncLocalValueChangedArgs(T? previousValue, T? currentValue, bool contextChanged)
        {
            PreviousValue = previousValue;
            CurrentValue = currentValue;
            ThreadContextChanged = contextChanged;
        }
    }
}
