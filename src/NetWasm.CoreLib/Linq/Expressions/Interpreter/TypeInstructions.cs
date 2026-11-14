// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: type tests and checked conversions run on the runtime type table. The interpreter holds a
// nullable as null or its boxed underlying value, so a test against Nullable<T> tests against T.

namespace System.Linq.Expressions.Interpreter
{
    // TypeIs: the value is not null and its type is assignable to the operand type.
    internal sealed class TypeIsInstruction(Type type) : Instruction
    {
        private readonly Type _type = Nullable.GetUnderlyingType(type) ?? type;

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(_type.IsInstanceOfType(frame.Pop()));
            return 1;
        }
    }

    // TypeEqual: the value is not null and its exact type is the operand type.
    internal sealed class TypeEqualInstruction(Type type) : Instruction
    {
        private readonly Type _type = Nullable.GetUnderlyingType(type) ?? type;

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            frame.Push(value is not null && value.GetType() == _type);
            return 1;
        }
    }

    // TypeAs: the value when it is an instance of the type, otherwise null.
    internal sealed class TypeAsInstruction(Type type) : Instruction
    {
        private readonly Type _type = Nullable.GetUnderlyingType(type) ?? type;

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            frame.Push(_type.IsInstanceOfType(value) ? value : null);
            return 1;
        }
    }

    // A downcast, an interface cast or an unboxing conversion, checked as the CLR checks castclass and unbox.any:
    // null passes to a reference or nullable type and throws NullReferenceException for a non-nullable value type;
    // a value of another type throws InvalidCastException.
    internal sealed class CastInstruction(Type type) : Instruction
    {
        private readonly Type _target = Nullable.GetUnderlyingType(type) ?? type;
        private readonly bool _acceptsNull = !type.IsValueType || type.IsNullable;

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            if (value is null)
            {
                if (!_acceptsNull)
                {
                    throw new NullReferenceException();
                }
                frame.Push(null);
                return 1;
            }
            if (!_target.IsInstanceOfType(value))
            {
                throw new InvalidCastException(
                    "Unable to cast object of type '" + value.GetType().ToString() + "' to type '" + _target.ToString() + "'.");
            }
            frame.Push(value);
            return 1;
        }
    }
}
