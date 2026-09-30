// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class LoadFieldInstruction(FieldInfo field) : Instruction
    {
        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(RuntimeMemberExecution.ReadField(field, frame.Pop()));
            return 1;
        }
    }

    internal sealed class CallInstruction : Instruction
    {
        private readonly MethodInfo _method;
        private readonly int _argumentCount;

        internal CallInstruction(MethodInfo method, int argumentCount)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentOutOfRangeException.ThrowIfNegative(argumentCount);
            _method = method;
            _argumentCount = argumentCount;
        }

        internal override int ConsumedStack =>
            _argumentCount + (_method.IsStatic ? 0 : 1);

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var first = frame.StackIndex - ConsumedStack;
            var receiver = _method.IsStatic ? null : frame.Data[first];
            object?[]? arguments = null;
            if (_argumentCount != 0)
            {
                arguments = new object?[_argumentCount];
                var argumentBase = first + (_method.IsStatic ? 0 : 1);
                for (var index = 0; index < arguments.Length; index++)
                {
                    arguments[index] = frame.Data[argumentBase + index];
                }
            }

            var result = RuntimeMemberExecution.InvokeMethod(
                _method,
                receiver,
                arguments);
            for (var index = first; index < frame.StackIndex; index++)
            {
                frame.Data[index] = null;
            }
            frame.StackIndex = first;
            frame.Push(result);
            return 1;
        }
    }
}
