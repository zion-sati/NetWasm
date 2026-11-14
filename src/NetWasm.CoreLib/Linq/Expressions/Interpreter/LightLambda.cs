// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Linq.Expressions.Interpreter
{
    // A nested lambda's target carries the boxes of the variables it captures.
    internal sealed class LightLambda(Interpreter interpreter, int parameterCount, StrongBox<object?>[]? closure = null) :
        ObjectArrayDelegateTarget
    {
        internal override object? InvokeCore(object?[] arguments)
        {
            if (arguments.Length != parameterCount)
            {
                throw new ArgumentException(
                    "The interpreted lambda received the wrong number of arguments.",
                    nameof(arguments));
            }
            return interpreter.Run(arguments, closure);
        }
    }
}
