// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class LightLambda(Interpreter interpreter) :
        ObjectArrayDelegateTarget
    {
        internal override object? InvokeCore(object?[] arguments)
        {
            if (arguments.Length != 1)
            {
                throw new ArgumentException(
                    "The interpreted lambda requires exactly one argument.",
                    nameof(arguments));
            }
            return interpreter.Run(arguments);
        }
    }
}
