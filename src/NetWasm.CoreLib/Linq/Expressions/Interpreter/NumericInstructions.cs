// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class AddInstruction : Instruction
    {
        private readonly TypeCode _typeCode;

        private AddInstruction(TypeCode typeCode) => _typeCode = typeCode;

        internal override int ConsumedStack => 2;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var right = frame.Pop()!;
            var left = frame.Pop()!;
            object result = _typeCode switch
            {
                TypeCode.Int16 => (object)unchecked(
                    (short)((short)left + (short)right)),
                TypeCode.UInt16 => (object)unchecked(
                    (ushort)((ushort)left + (ushort)right)),
                TypeCode.Int32 => (object)unchecked((int)left + (int)right),
                TypeCode.UInt32 => (object)unchecked((uint)left + (uint)right),
                TypeCode.Int64 => (object)unchecked((long)left + (long)right),
                TypeCode.UInt64 => (object)unchecked((ulong)left + (ulong)right),
                TypeCode.Single => (object)((float)left + (float)right),
                TypeCode.Double => (object)((double)left + (double)right),
                _ => throw InvalidNumericInstruction(),
            };
            frame.Push(result);
            return 1;
        }

        internal static Instruction Create(Type type) =>
            new AddInstruction(Type.GetTypeCode(type));

        private static InvalidOperationException InvalidNumericInstruction() =>
            new("The interpreted numeric instruction has an invalid type.");
    }

    internal sealed class NegateInstruction : Instruction
    {
        private readonly TypeCode _typeCode;

        private NegateInstruction(TypeCode typeCode) => _typeCode = typeCode;

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop()!;
            object result = _typeCode switch
            {
                TypeCode.Int16 => (object)unchecked((short)-(short)value),
                TypeCode.Int32 => (object)unchecked(-(int)value),
                TypeCode.Int64 => (object)unchecked(-(long)value),
                TypeCode.Single => (object)-(float)value,
                TypeCode.Double => (object)-(double)value,
                _ => throw InvalidNumericInstruction(),
            };
            frame.Push(result);
            return 1;
        }

        internal static Instruction Create(Type type) =>
            new NegateInstruction(Type.GetTypeCode(type));

        private static InvalidOperationException InvalidNumericInstruction() =>
            new("The interpreted numeric instruction has an invalid type.");
    }

    internal enum NumericComparison
    {
        LessThan,
        GreaterThan,
    }

    internal sealed class ComparisonInstruction : Instruction
    {
        private readonly TypeCode _typeCode;
        private readonly NumericComparison _comparison;

        private ComparisonInstruction(
            TypeCode typeCode,
            NumericComparison comparison)
        {
            _typeCode = typeCode;
            _comparison = comparison;
        }

        internal override int ConsumedStack => 2;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var right = frame.Pop()!;
            var left = frame.Pop()!;
            var result = _comparison == NumericComparison.LessThan
                ? IsLessThan(left, right)
                : IsGreaterThan(left, right);
            frame.Push(result);
            return 1;
        }

        internal static Instruction Create(
            Type type,
            NumericComparison comparison) =>
            new ComparisonInstruction(Type.GetTypeCode(type), comparison);

        private bool IsLessThan(object left, object right) => _typeCode switch
        {
            TypeCode.Int16 => (short)left < (short)right,
            TypeCode.UInt16 => (ushort)left < (ushort)right,
            TypeCode.Int32 => (int)left < (int)right,
            TypeCode.UInt32 => (uint)left < (uint)right,
            TypeCode.Int64 => (long)left < (long)right,
            TypeCode.UInt64 => (ulong)left < (ulong)right,
            TypeCode.Single => (float)left < (float)right,
            TypeCode.Double => (double)left < (double)right,
            _ => throw InvalidNumericInstruction(),
        };

        private bool IsGreaterThan(object left, object right) => _typeCode switch
        {
            TypeCode.Int16 => (short)left > (short)right,
            TypeCode.UInt16 => (ushort)left > (ushort)right,
            TypeCode.Int32 => (int)left > (int)right,
            TypeCode.UInt32 => (uint)left > (uint)right,
            TypeCode.Int64 => (long)left > (long)right,
            TypeCode.UInt64 => (ulong)left > (ulong)right,
            TypeCode.Single => (float)left > (float)right,
            TypeCode.Double => (double)left > (double)right,
            _ => throw InvalidNumericInstruction(),
        };

        private static InvalidOperationException InvalidNumericInstruction() =>
            new("The interpreted numeric instruction has an invalid type.");
    }

    internal sealed class NumericConvertInstruction : Instruction
    {
        private readonly TypeCode _from;
        private readonly TypeCode _to;

        internal NumericConvertInstruction(TypeCode from, TypeCode to)
        {
            _from = from;
            _to = to;
        }

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop()!;
            frame.Push(Convert(value));
            return 1;
        }

        private object Convert(object value) => _from switch
        {
            TypeCode.SByte => ConvertInt32((sbyte)value),
            TypeCode.Byte => ConvertInt32((byte)value),
            TypeCode.Int16 => ConvertInt32((short)value),
            TypeCode.UInt16 => ConvertInt32((ushort)value),
            TypeCode.Int32 => ConvertInt32((int)value),
            TypeCode.UInt32 => ConvertInt64((uint)value),
            TypeCode.Int64 => ConvertInt64((long)value),
            TypeCode.UInt64 => ConvertUInt64((ulong)value),
            TypeCode.Single => ConvertDouble((float)value),
            TypeCode.Double => ConvertDouble((double)value),
            _ => throw InvalidNumericInstruction(),
        };

        private object ConvertInt32(int value)
        {
            unchecked
            {
                return _to switch
                {
                    TypeCode.SByte => (object)(sbyte)value,
                    TypeCode.Byte => (object)(byte)value,
                    TypeCode.Int16 => (object)(short)value,
                    TypeCode.UInt16 => (object)(ushort)value,
                    TypeCode.Int32 => (object)value,
                    TypeCode.UInt32 => (object)(uint)value,
                    TypeCode.Int64 => (object)(long)value,
                    TypeCode.UInt64 => (object)(ulong)value,
                    TypeCode.Single => (object)(float)value,
                    TypeCode.Double => (object)(double)value,
                    _ => throw InvalidNumericInstruction(),
                };
            }
        }

        private object ConvertInt64(long value)
        {
            unchecked
            {
                return _to switch
                {
                    TypeCode.SByte => (object)(sbyte)value,
                    TypeCode.Byte => (object)(byte)value,
                    TypeCode.Int16 => (object)(short)value,
                    TypeCode.UInt16 => (object)(ushort)value,
                    TypeCode.Int32 => (object)(int)value,
                    TypeCode.UInt32 => (object)(uint)value,
                    TypeCode.Int64 => (object)value,
                    TypeCode.UInt64 => (object)(ulong)value,
                    TypeCode.Single => (object)(float)value,
                    TypeCode.Double => (object)(double)value,
                    _ => throw InvalidNumericInstruction(),
                };
            }
        }

        private object ConvertUInt64(ulong value)
        {
            unchecked
            {
                return _to switch
                {
                    TypeCode.SByte => (object)(sbyte)value,
                    TypeCode.Byte => (object)(byte)value,
                    TypeCode.Int16 => (object)(short)value,
                    TypeCode.UInt16 => (object)(ushort)value,
                    TypeCode.Int32 => (object)(int)value,
                    TypeCode.UInt32 => (object)(uint)value,
                    TypeCode.Int64 => (object)(long)value,
                    TypeCode.UInt64 => (object)value,
                    TypeCode.Single => (object)(float)value,
                    TypeCode.Double => (object)(double)value,
                    _ => throw InvalidNumericInstruction(),
                };
            }
        }

        private object ConvertDouble(double value)
        {
            unchecked
            {
                return _to switch
                {
                    TypeCode.SByte => (object)(sbyte)value,
                    TypeCode.Byte => (object)(byte)value,
                    TypeCode.Int16 => (object)(short)value,
                    TypeCode.UInt16 => (object)(ushort)value,
                    TypeCode.Int32 => (object)(int)value,
                    TypeCode.UInt32 => (object)(uint)value,
                    TypeCode.Int64 => (object)(long)value,
                    TypeCode.UInt64 => (object)(ulong)value,
                    TypeCode.Single => (object)(float)value,
                    TypeCode.Double => (object)value,
                    _ => throw InvalidNumericInstruction(),
                };
            }
        }

        private static InvalidOperationException InvalidNumericInstruction() =>
            new("The interpreted numeric instruction has an invalid type.");
    }
}
