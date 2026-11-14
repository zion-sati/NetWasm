// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class StateMachineAttribute : Attribute
    {
        public StateMachineAttribute(Type stateMachineType) =>
            StateMachineType = stateMachineType;

        public Type StateMachineType { get; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class AsyncIteratorStateMachineAttribute : StateMachineAttribute
    {
        public AsyncIteratorStateMachineAttribute(Type stateMachineType)
            : base(stateMachineType)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class IteratorStateMachineAttribute : StateMachineAttribute
    {
        public IteratorStateMachineAttribute(Type stateMachineType)
            : base(stateMachineType)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class EnumeratorCancellationAttribute : Attribute
    {
        public EnumeratorCancellationAttribute()
        {
        }
    }
}
