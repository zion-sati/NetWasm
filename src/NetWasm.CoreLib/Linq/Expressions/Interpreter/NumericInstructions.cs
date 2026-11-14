// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    // NetWasm: one instruction per operator arity, switching on the operand's TypeCode, instead of
    // upstream's per-operator, per-type instruction classes. The interpreter keeps a nullable as null
    // or its boxed value, so a null operand of a lifted operator yields null, except that lifted Boolean
    // And and Or follow upstream's three-valued logic.
    internal sealed class BinaryOperatorInstruction : Instruction
    {
        private readonly ExpressionType _operator;
        private readonly TypeCode _typeCode;

        internal BinaryOperatorInstruction(ExpressionType @operator, Type type)
        {
            _operator = @operator;
            _typeCode = Type.GetTypeCode(type);
        }

        internal override int ConsumedStack => 2;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var right = frame.Pop();
            var left = frame.Pop();
            if (_typeCode == TypeCode.Boolean &&
                _operator is ExpressionType.And or ExpressionType.Or)
            {
                frame.Push(_operator == ExpressionType.And
                    ? LiftedAnd(left, right)
                    : LiftedOr(left, right));
                return 1;
            }
            if (left is null || right is null)
            {
                frame.Push(null);
                return 1;
            }
            frame.Push(_operator switch
            {
                ExpressionType.Add => Add(left, right),
                ExpressionType.AddChecked => AddChecked(left, right),
                ExpressionType.Subtract => Subtract(left, right),
                ExpressionType.SubtractChecked => SubtractChecked(left, right),
                ExpressionType.Multiply => Multiply(left, right),
                ExpressionType.MultiplyChecked => MultiplyChecked(left, right),
                ExpressionType.Divide => Divide(left, right),
                ExpressionType.Modulo => Modulo(left, right),
                ExpressionType.And => And(left, right),
                ExpressionType.Or => Or(left, right),
                ExpressionType.ExclusiveOr => ExclusiveOr(left, right),
                ExpressionType.LeftShift => LeftShift(left, right),
                ExpressionType.RightShift => RightShift(left, right),
                _ => throw InvalidNumericInstruction(),
            });
            return 1;
        }

        private static bool? LiftedAnd(object? left, object? right)
        {
            if (left is false || right is false)
            {
                return false;
            }
            return left is null || right is null ? null : true;
        }

        private static bool? LiftedOr(object? left, object? right)
        {
            if (left is true || right is true)
            {
                return true;
            }
            return left is null || right is null ? null : false;
        }

        private object Add(object l, object r) => _typeCode switch
        {
            TypeCode.Int16 => (object)unchecked((short)((short)l + (short)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l + (ushort)r)),
            TypeCode.Int32 => (object)unchecked((int)l + (int)r),
            TypeCode.UInt32 => (object)unchecked((uint)l + (uint)r),
            TypeCode.Int64 => (object)unchecked((long)l + (long)r),
            TypeCode.UInt64 => (object)unchecked((ulong)l + (ulong)r),
            TypeCode.Single => (object)((float)l + (float)r),
            TypeCode.Double => (object)((double)l + (double)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object AddChecked(object l, object r) => _typeCode switch
        {
            TypeCode.Int16 => (object)checked((short)((short)l + (short)r)),
            TypeCode.UInt16 => (object)checked((ushort)((ushort)l + (ushort)r)),
            TypeCode.Int32 => (object)checked((int)l + (int)r),
            TypeCode.UInt32 => (object)checked((uint)l + (uint)r),
            TypeCode.Int64 => (object)checked((long)l + (long)r),
            TypeCode.UInt64 => (object)checked((ulong)l + (ulong)r),
            TypeCode.Single => (object)((float)l + (float)r),
            TypeCode.Double => (object)((double)l + (double)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object Subtract(object l, object r) => _typeCode switch
        {
            TypeCode.Int16 => (object)unchecked((short)((short)l - (short)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l - (ushort)r)),
            TypeCode.Int32 => (object)unchecked((int)l - (int)r),
            TypeCode.UInt32 => (object)unchecked((uint)l - (uint)r),
            TypeCode.Int64 => (object)unchecked((long)l - (long)r),
            TypeCode.UInt64 => (object)unchecked((ulong)l - (ulong)r),
            TypeCode.Single => (object)((float)l - (float)r),
            TypeCode.Double => (object)((double)l - (double)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object SubtractChecked(object l, object r) => _typeCode switch
        {
            TypeCode.Int16 => (object)checked((short)((short)l - (short)r)),
            TypeCode.UInt16 => (object)checked((ushort)((ushort)l - (ushort)r)),
            TypeCode.Int32 => (object)checked((int)l - (int)r),
            TypeCode.UInt32 => (object)checked((uint)l - (uint)r),
            TypeCode.Int64 => (object)checked((long)l - (long)r),
            TypeCode.UInt64 => (object)checked((ulong)l - (ulong)r),
            TypeCode.Single => (object)((float)l - (float)r),
            TypeCode.Double => (object)((double)l - (double)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object Multiply(object l, object r) => _typeCode switch
        {
            TypeCode.Int16 => (object)unchecked((short)((short)l * (short)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l * (ushort)r)),
            TypeCode.Int32 => (object)unchecked((int)l * (int)r),
            TypeCode.UInt32 => (object)unchecked((uint)l * (uint)r),
            TypeCode.Int64 => (object)unchecked((long)l * (long)r),
            TypeCode.UInt64 => (object)unchecked((ulong)l * (ulong)r),
            TypeCode.Single => (object)((float)l * (float)r),
            TypeCode.Double => (object)((double)l * (double)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object MultiplyChecked(object l, object r) => _typeCode switch
        {
            TypeCode.Int16 => (object)checked((short)((short)l * (short)r)),
            TypeCode.UInt16 => (object)checked((ushort)((ushort)l * (ushort)r)),
            TypeCode.Int32 => (object)checked((int)l * (int)r),
            TypeCode.UInt32 => (object)checked((uint)l * (uint)r),
            TypeCode.Int64 => (object)checked((long)l * (long)r),
            TypeCode.UInt64 => (object)checked((ulong)l * (ulong)r),
            TypeCode.Single => (object)((float)l * (float)r),
            TypeCode.Double => (object)((double)l * (double)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object Divide(object l, object r) => _typeCode switch
        {
            TypeCode.Int16 => (object)unchecked((short)((short)l / (short)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l / (ushort)r)),
            TypeCode.Int32 => (object)unchecked((int)l / (int)r),
            TypeCode.UInt32 => (object)unchecked((uint)l / (uint)r),
            TypeCode.Int64 => (object)unchecked((long)l / (long)r),
            TypeCode.UInt64 => (object)unchecked((ulong)l / (ulong)r),
            TypeCode.Single => (object)((float)l / (float)r),
            TypeCode.Double => (object)((double)l / (double)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object Modulo(object l, object r) => _typeCode switch
        {
            TypeCode.Int16 => (object)unchecked((short)((short)l % (short)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l % (ushort)r)),
            TypeCode.Int32 => (object)unchecked((int)l % (int)r),
            TypeCode.UInt32 => (object)unchecked((uint)l % (uint)r),
            TypeCode.Int64 => (object)unchecked((long)l % (long)r),
            TypeCode.UInt64 => (object)unchecked((ulong)l % (ulong)r),
            TypeCode.Single => (object)((float)l % (float)r),
            TypeCode.Double => (object)((double)l % (double)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object And(object l, object r) => _typeCode switch
        {
            TypeCode.SByte => (object)unchecked((sbyte)((sbyte)l & (sbyte)r)),
            TypeCode.Byte => (object)unchecked((byte)((byte)l & (byte)r)),
            TypeCode.Int16 => (object)unchecked((short)((short)l & (short)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l & (ushort)r)),
            TypeCode.Int32 => (object)unchecked((int)l & (int)r),
            TypeCode.UInt32 => (object)unchecked((uint)l & (uint)r),
            TypeCode.Int64 => (object)unchecked((long)l & (long)r),
            TypeCode.UInt64 => (object)unchecked((ulong)l & (ulong)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object Or(object l, object r) => _typeCode switch
        {
            TypeCode.SByte => (object)unchecked((sbyte)((sbyte)l | (sbyte)r)),
            TypeCode.Byte => (object)unchecked((byte)((byte)l | (byte)r)),
            TypeCode.Int16 => (object)unchecked((short)((short)l | (short)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l | (ushort)r)),
            TypeCode.Int32 => (object)unchecked((int)l | (int)r),
            TypeCode.UInt32 => (object)unchecked((uint)l | (uint)r),
            TypeCode.Int64 => (object)unchecked((long)l | (long)r),
            TypeCode.UInt64 => (object)unchecked((ulong)l | (ulong)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object ExclusiveOr(object l, object r) => _typeCode switch
        {
            TypeCode.SByte => (object)unchecked((sbyte)((sbyte)l ^ (sbyte)r)),
            TypeCode.Byte => (object)unchecked((byte)((byte)l ^ (byte)r)),
            TypeCode.Int16 => (object)unchecked((short)((short)l ^ (short)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l ^ (ushort)r)),
            TypeCode.Int32 => (object)unchecked((int)l ^ (int)r),
            TypeCode.UInt32 => (object)unchecked((uint)l ^ (uint)r),
            TypeCode.Int64 => (object)unchecked((long)l ^ (long)r),
            TypeCode.UInt64 => (object)unchecked((ulong)l ^ (ulong)r),
            TypeCode.Boolean => (object)((bool)l ^ (bool)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object LeftShift(object l, object r) => _typeCode switch
        {
            TypeCode.SByte => (object)unchecked((sbyte)((sbyte)l << (int)r)),
            TypeCode.Byte => (object)unchecked((byte)((byte)l << (int)r)),
            TypeCode.Int16 => (object)unchecked((short)((short)l << (int)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l << (int)r)),
            TypeCode.Int32 => (object)((int)l << (int)r),
            TypeCode.UInt32 => (object)((uint)l << (int)r),
            TypeCode.Int64 => (object)((long)l << (int)r),
            TypeCode.UInt64 => (object)((ulong)l << (int)r),
            _ => throw InvalidNumericInstruction(),
        };

        private object RightShift(object l, object r) => _typeCode switch
        {
            TypeCode.SByte => (object)unchecked((sbyte)((sbyte)l >> (int)r)),
            TypeCode.Byte => (object)unchecked((byte)((byte)l >> (int)r)),
            TypeCode.Int16 => (object)unchecked((short)((short)l >> (int)r)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)l >> (int)r)),
            TypeCode.Int32 => (object)((int)l >> (int)r),
            TypeCode.UInt32 => (object)((uint)l >> (int)r),
            TypeCode.Int64 => (object)((long)l >> (int)r),
            TypeCode.UInt64 => (object)((ulong)l >> (int)r),
            _ => throw InvalidNumericInstruction(),
        };

        private static InvalidOperationException InvalidNumericInstruction() =>
            new("The interpreted numeric instruction has an invalid type.");
    }

    internal sealed class UnaryOperatorInstruction : Instruction
    {
        private readonly ExpressionType _operator;
        private readonly TypeCode _typeCode;

        internal UnaryOperatorInstruction(ExpressionType @operator, Type type)
        {
            _operator = @operator;
            _typeCode = Type.GetTypeCode(type);
        }

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            if (value is null)
            {
                frame.Push(null);
                return 1;
            }
            frame.Push(_operator switch
            {
                ExpressionType.Negate => Negate(value),
                ExpressionType.NegateChecked => NegateChecked(value),
                ExpressionType.Not => _typeCode == TypeCode.Boolean ? !(bool)value : OnesComplement(value),
                ExpressionType.OnesComplement => OnesComplement(value),
                ExpressionType.Increment => Increment(value),
                ExpressionType.Decrement => Decrement(value),
                ExpressionType.IsTrue => (bool)value,
                ExpressionType.IsFalse => !(bool)value,
                _ => throw InvalidNumericInstruction(),
            });
            return 1;
        }

        private object Negate(object v) => _typeCode switch
        {
            TypeCode.Int16 => (object)unchecked((short)-(short)v),
            TypeCode.Int32 => (object)unchecked(-(int)v),
            TypeCode.Int64 => (object)unchecked(-(long)v),
            TypeCode.Single => (object)(float)-(float)v,
            TypeCode.Double => (object)(double)-(double)v,
            _ => throw InvalidNumericInstruction(),
        };

        private object NegateChecked(object v) => _typeCode switch
        {
            TypeCode.Int16 => (object)checked((short)-(short)v),
            TypeCode.Int32 => (object)checked(-(int)v),
            TypeCode.Int64 => (object)checked(-(long)v),
            TypeCode.Single => (object)(float)-(float)v,
            TypeCode.Double => (object)(double)-(double)v,
            _ => throw InvalidNumericInstruction(),
        };

        private object OnesComplement(object v) => _typeCode switch
        {
            TypeCode.SByte => (object)unchecked((sbyte)~(sbyte)v),
            TypeCode.Byte => (object)unchecked((byte)~(byte)v),
            TypeCode.Int16 => (object)unchecked((short)~(short)v),
            TypeCode.UInt16 => (object)unchecked((ushort)~(ushort)v),
            TypeCode.Int32 => (object)~(int)v,
            TypeCode.UInt32 => (object)~(uint)v,
            TypeCode.Int64 => (object)~(long)v,
            TypeCode.UInt64 => (object)~(ulong)v,
            _ => throw InvalidNumericInstruction(),
        };

        private object Increment(object v) => _typeCode switch
        {
            TypeCode.Int16 => (object)unchecked((short)((short)v + 1)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)v + 1)),
            TypeCode.Int32 => (object)unchecked((int)v + 1),
            TypeCode.UInt32 => (object)unchecked((uint)v + 1),
            TypeCode.Int64 => (object)unchecked((long)v + 1),
            TypeCode.UInt64 => (object)unchecked((ulong)v + 1),
            TypeCode.Single => (object)((float)v + 1),
            TypeCode.Double => (object)((double)v + 1),
            _ => throw InvalidNumericInstruction(),
        };

        private object Decrement(object v) => _typeCode switch
        {
            TypeCode.Int16 => (object)unchecked((short)((short)v - 1)),
            TypeCode.UInt16 => (object)unchecked((ushort)((ushort)v - 1)),
            TypeCode.Int32 => (object)unchecked((int)v - 1),
            TypeCode.UInt32 => (object)unchecked((uint)v - 1),
            TypeCode.Int64 => (object)unchecked((long)v - 1),
            TypeCode.UInt64 => (object)unchecked((ulong)v - 1),
            TypeCode.Single => (object)((float)v - 1),
            TypeCode.Double => (object)((double)v - 1),
            _ => throw InvalidNumericInstruction(),
        };

        private static InvalidOperationException InvalidNumericInstruction() =>
            new("The interpreted numeric instruction has an invalid type.");
    }

    internal enum NumericComparison
    {
        LessThan,
        GreaterThan,
        LessThanOrEqual,
        GreaterThanOrEqual,
    }

    // NetWasm: a lifted comparison with a null operand is false, or null when lifted to null.
    internal sealed class ComparisonInstruction : Instruction
    {
        private readonly TypeCode _typeCode;
        private readonly NumericComparison _comparison;
        private readonly bool _liftedToNull;

        internal ComparisonInstruction(
            Type type,
            NumericComparison comparison,
            bool liftedToNull)
        {
            _typeCode = Type.GetTypeCode(type);
            _comparison = comparison;
            _liftedToNull = liftedToNull;
        }

        internal override int ConsumedStack => 2;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var right = frame.Pop();
            var left = frame.Pop();
            if (left is null || right is null)
            {
                frame.Push(_liftedToNull ? null : false);
                return 1;
            }
            frame.Push(_typeCode switch
            {
                TypeCode.Single => Compare((float)left, (float)right),
                TypeCode.Double => Compare((double)left, (double)right),
                TypeCode.UInt64 => Compare((ulong)left, (ulong)right),
                _ => Compare(ToInt64(left), ToInt64(right)),
            });
            return 1;
        }

        private long ToInt64(object value) => _typeCode switch
        {
            TypeCode.Char => (char)value,
            TypeCode.SByte => (sbyte)value,
            TypeCode.Byte => (byte)value,
            TypeCode.Int16 => (short)value,
            TypeCode.UInt16 => (ushort)value,
            TypeCode.Int32 => (int)value,
            TypeCode.UInt32 => (uint)value,
            TypeCode.Int64 => (long)value,
            _ => throw InvalidNumericInstruction(),
        };

        private bool Compare(long left, long right) => _comparison switch
        {
            NumericComparison.LessThan => left < right,
            NumericComparison.GreaterThan => left > right,
            NumericComparison.LessThanOrEqual => left <= right,
            _ => left >= right,
        };

        private bool Compare(ulong left, ulong right) => _comparison switch
        {
            NumericComparison.LessThan => left < right,
            NumericComparison.GreaterThan => left > right,
            NumericComparison.LessThanOrEqual => left <= right,
            _ => left >= right,
        };

        private bool Compare(double left, double right) => _comparison switch
        {
            NumericComparison.LessThan => left < right,
            NumericComparison.GreaterThan => left > right,
            NumericComparison.LessThanOrEqual => left <= right,
            _ => left >= right,
        };

        private static InvalidOperationException InvalidNumericInstruction() =>
            new("The interpreted numeric instruction has an invalid type.");
    }

    internal sealed class NumericConvertInstruction : Instruction
    {
        private readonly TypeCode _from;
        private readonly TypeCode _to;
        private readonly bool _liftedToNull;
        private readonly bool _checked;

        internal NumericConvertInstruction(TypeCode from, TypeCode to, bool liftedToNull, bool isChecked = false)
        {
            _from = from;
            _to = to;
            _liftedToNull = liftedToNull;
            _checked = isChecked;
        }

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            if (value is null && !_liftedToNull)
            {
                throw new InvalidOperationException("Nullable object must have a value.");
            }
            frame.Push(value is null ? null : _checked ? ConvertChecked(value) : Convert(value));
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

        private object ConvertChecked(object value) => _from switch
        {
            TypeCode.SByte => ConvertInt32Checked((sbyte)value),
            TypeCode.Byte => ConvertInt32Checked((byte)value),
            TypeCode.Int16 => ConvertInt32Checked((short)value),
            TypeCode.UInt16 => ConvertInt32Checked((ushort)value),
            TypeCode.Int32 => ConvertInt32Checked((int)value),
            TypeCode.UInt32 => ConvertInt64Checked((uint)value),
            TypeCode.Int64 => ConvertInt64Checked((long)value),
            TypeCode.UInt64 => ConvertUInt64Checked((ulong)value),
            TypeCode.Single => ConvertDoubleChecked((float)value),
            TypeCode.Double => ConvertDoubleChecked((double)value),
            _ => throw InvalidNumericInstruction(),
        };

        private object ConvertInt32Checked(int value)
        {
            checked
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

        private object ConvertInt64Checked(long value)
        {
            checked
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

        private object ConvertUInt64Checked(ulong value)
        {
            checked
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

        private object ConvertDoubleChecked(double value)
        {
            checked
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
