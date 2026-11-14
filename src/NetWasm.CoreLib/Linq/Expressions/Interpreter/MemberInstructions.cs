// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions.Interpreter
{
    // A field of the receiver, an object or a struct's box; a static field has no receiver.
    internal sealed class LoadFieldInstruction(FieldInfo field) : Instruction
    {
        private readonly bool _isStatic = field.IsStatic;

        internal override int ConsumedStack => _isStatic ? 0 : 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(RuntimeMemberExecution.ReadField(field, _isStatic ? null : frame.Pop()));
            return 1;
        }
    }

    // A method on its receiver (a struct's runs on the box's contents), or a constructor, which takes no receiver
    // and whose new object or box is the result. A void method produces nothing.
    internal sealed class CallInstruction : Instruction
    {
        private readonly MethodBase _method;
        private readonly int _argumentCount;
        private readonly bool _hasReceiver;
        private readonly bool _producesValue;

        internal CallInstruction(MethodBase method, int argumentCount, bool producesValue)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentOutOfRangeException.ThrowIfNegative(argumentCount);
            _method = method;
            _argumentCount = argumentCount;
            _hasReceiver = !method.IsStatic && method is not ConstructorInfo;
            _producesValue = producesValue;
        }

        internal override int ConsumedStack => _argumentCount + (_hasReceiver ? 1 : 0);

        internal override int ProducedStack => _producesValue ? 1 : 0;

        internal override int Run(InterpretedFrame frame)
        {
            var first = frame.StackIndex - ConsumedStack;
            var receiver = _hasReceiver ? frame.Data[first] : null;
            object?[]? arguments = null;
            if (_argumentCount != 0)
            {
                arguments = new object?[_argumentCount];
                var argumentBase = first + (_hasReceiver ? 1 : 0);
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
            if (_producesValue)
            {
                frame.Push(result);
            }
            return 1;
        }
    }

    // Assigns the value to a field of the receiver (an object, or a struct's box, changed in place) or a static field;
    // the value stays for an assignment whose result is used.
    internal sealed class StoreFieldInstruction(FieldInfo field, bool preservesValue) : Instruction
    {
        private readonly bool _isStatic = field.IsStatic;

        internal override int ConsumedStack => _isStatic ? 1 : 2;

        internal override int ProducedStack => preservesValue ? 1 : 0;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            var receiver = _isStatic ? null : frame.Pop();
            RuntimeMemberExecution.WriteField(field, receiver, value);
            if (preservesValue)
            {
                frame.Push(value);
            }
            return 1;
        }
    }

    // Assigns the value through a property's setter, which runs as a void call.
    internal sealed class StorePropertyInstruction(MethodInfo setter, bool preservesValue) : Instruction
    {
        private readonly bool _isStatic = setter.IsStatic;

        internal override int ConsumedStack => _isStatic ? 1 : 2;

        internal override int ProducedStack => preservesValue ? 1 : 0;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            var receiver = _isStatic ? null : frame.Pop();
            RuntimeMemberExecution.InvokeMethod(setter, receiver, [value]);
            if (preservesValue)
            {
                frame.Push(value);
            }
            return 1;
        }
    }
}
