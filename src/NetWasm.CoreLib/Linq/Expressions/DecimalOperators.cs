// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: upstream's expression factories find decimal's operator methods by name through reflection
// (op_Addition, op_Implicit and the rest), which NetWasm does not carry. Decimal's binary operators and its ++ and --
// and unary plus are described here by DecimalOperatorMethod, a MethodInfo that runs the operator when invoked (C#
// keeps no node for a unary plus, so no tree names that method). Its unary minus and its conversions are decimal's own
// methods, taken once from a C# expression tree (C# names them by token
// there), so a tree C# builds and one the factories build carry the same MethodInfo. The interpreter runs both kinds
// itself (Classify).

using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace System.Linq.Expressions
{
    internal enum DecimalOperatorKind
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        Negate,
        UnaryPlus,
        Increment,
        Decrement,
        Convert,
    }

    internal static class DecimalOperators
    {
        private static DecimalOperatorMethod[]? s_synthetic;
        private static Captured? s_captured;

        // The binary operator decimal declares under the name, or null for a name it does not declare.
        internal static MethodInfo? Binary(string name) => name switch
        {
            "op_Addition" => Synthetic(DecimalOperatorKind.Add),
            "op_Subtraction" => Synthetic(DecimalOperatorKind.Subtract),
            "op_Multiply" => Synthetic(DecimalOperatorKind.Multiply),
            "op_Division" => Synthetic(DecimalOperatorKind.Divide),
            "op_Modulus" => Synthetic(DecimalOperatorKind.Modulo),
            "op_Equality" => Synthetic(DecimalOperatorKind.Equal),
            "op_Inequality" => Synthetic(DecimalOperatorKind.NotEqual),
            "op_LessThan" => Synthetic(DecimalOperatorKind.LessThan),
            "op_LessThanOrEqual" => Synthetic(DecimalOperatorKind.LessThanOrEqual),
            "op_GreaterThan" => Synthetic(DecimalOperatorKind.GreaterThan),
            "op_GreaterThanOrEqual" => Synthetic(DecimalOperatorKind.GreaterThanOrEqual),
            _ => null,
        };

        // The unary operator decimal declares under the name, or null.
        internal static MethodInfo? Unary(string name) => name switch
        {
            "op_UnaryNegation" => Capture().Negate,
            "op_UnaryPlus" => Synthetic(DecimalOperatorKind.UnaryPlus),
            "op_Increment" => Synthetic(DecimalOperatorKind.Increment),
            "op_Decrement" => Synthetic(DecimalOperatorKind.Decrement),
            _ => null,
        };

        // Decimal's implicit or explicit conversion between the two non-nullable types, or null where it declares none.
        internal static MethodInfo? Conversion(Type from, Type to) =>
            (from == typeof(decimal) || to == typeof(decimal)) &&
            Capture().Conversions.TryGetValue((Type.GetTypeCode(from), Type.GetTypeCode(to)), out var method)
                ? method
                : null;

        // Which of decimal's operators a node's method is, or null for any other method.
        internal static DecimalOperatorKind? Classify(MethodInfo? method)
        {
            if (method is DecimalOperatorMethod synthetic)
            {
                return synthetic.Kind;
            }
            if (method is null || method.DeclaringType != typeof(decimal))
            {
                return null;
            }
            return Capture().Kinds.TryGetValue(method, out var kind) ? kind : null;
        }

        internal static object Apply(DecimalOperatorKind kind, decimal left, decimal right) => kind switch
        {
            DecimalOperatorKind.Add => left + right,
            DecimalOperatorKind.Subtract => left - right,
            DecimalOperatorKind.Multiply => left * right,
            DecimalOperatorKind.Divide => left / right,
            DecimalOperatorKind.Modulo => left % right,
            DecimalOperatorKind.Equal => left == right,
            DecimalOperatorKind.NotEqual => left != right,
            DecimalOperatorKind.LessThan => left < right,
            DecimalOperatorKind.LessThanOrEqual => left <= right,
            DecimalOperatorKind.GreaterThan => left > right,
            DecimalOperatorKind.GreaterThanOrEqual => left >= right,
            _ => throw new InvalidOperationException("The decimal operator is not binary."),
        };

        internal static object Apply(DecimalOperatorKind kind, decimal operand) => kind switch
        {
            DecimalOperatorKind.Negate => -operand,
            DecimalOperatorKind.UnaryPlus => +operand,
            DecimalOperatorKind.Increment => operand + 1m,
            DecimalOperatorKind.Decrement => operand - 1m,
            _ => throw new InvalidOperationException("The decimal operator is not unary."),
        };

        // A conversion to or from decimal, as decimal's op_Implicit and op_Explicit compute it.
        internal static object Convert(object value, TypeCode from, TypeCode to) => from == TypeCode.Decimal
            ? to switch
            {
                TypeCode.Byte => (object)(byte)(decimal)value,
                TypeCode.SByte => (object)(sbyte)(decimal)value,
                TypeCode.Int16 => (object)(short)(decimal)value,
                TypeCode.UInt16 => (object)(ushort)(decimal)value,
                TypeCode.Char => (object)(char)(decimal)value,
                TypeCode.Int32 => (object)(int)(decimal)value,
                TypeCode.UInt32 => (object)(uint)(decimal)value,
                TypeCode.Int64 => (object)(long)(decimal)value,
                TypeCode.UInt64 => (object)(ulong)(decimal)value,
                TypeCode.Single => (object)(float)(decimal)value,
                TypeCode.Double => (object)(double)(decimal)value,
                _ => throw new InvalidOperationException("Decimal has no conversion to that type."),
            }
            : from switch
            {
                TypeCode.Byte => (decimal)(byte)value,
                TypeCode.SByte => (decimal)(sbyte)value,
                TypeCode.Int16 => (decimal)(short)value,
                TypeCode.UInt16 => (decimal)(ushort)value,
                TypeCode.Char => (decimal)(char)value,
                TypeCode.Int32 => (decimal)(int)value,
                TypeCode.UInt32 => (decimal)(uint)value,
                TypeCode.Int64 => (decimal)(long)value,
                TypeCode.UInt64 => (decimal)(ulong)value,
                TypeCode.Single => (decimal)(float)value,
                TypeCode.Double => (decimal)(double)value,
                _ => throw new InvalidOperationException("Decimal has no conversion from that type."),
            };

        private static DecimalOperatorMethod Synthetic(DecimalOperatorKind kind)
        {
            var methods = s_synthetic ??= Create();
            return methods[(int)kind];

            static DecimalOperatorMethod[] Create()
            {
                var created = new DecimalOperatorMethod[(int)DecimalOperatorKind.Decrement + 1];
                void Add(DecimalOperatorKind kind, string name, Type result, int arity) =>
                    created[(int)kind] = new DecimalOperatorMethod(kind, name, result, arity);
                Add(DecimalOperatorKind.Add, "op_Addition", typeof(decimal), 2);
                Add(DecimalOperatorKind.Subtract, "op_Subtraction", typeof(decimal), 2);
                Add(DecimalOperatorKind.Multiply, "op_Multiply", typeof(decimal), 2);
                Add(DecimalOperatorKind.Divide, "op_Division", typeof(decimal), 2);
                Add(DecimalOperatorKind.Modulo, "op_Modulus", typeof(decimal), 2);
                Add(DecimalOperatorKind.Equal, "op_Equality", typeof(bool), 2);
                Add(DecimalOperatorKind.NotEqual, "op_Inequality", typeof(bool), 2);
                Add(DecimalOperatorKind.LessThan, "op_LessThan", typeof(bool), 2);
                Add(DecimalOperatorKind.LessThanOrEqual, "op_LessThanOrEqual", typeof(bool), 2);
                Add(DecimalOperatorKind.GreaterThan, "op_GreaterThan", typeof(bool), 2);
                Add(DecimalOperatorKind.GreaterThanOrEqual, "op_GreaterThanOrEqual", typeof(bool), 2);
                Add(DecimalOperatorKind.UnaryPlus, "op_UnaryPlus", typeof(decimal), 1);
                Add(DecimalOperatorKind.Increment, "op_Increment", typeof(decimal), 1);
                Add(DecimalOperatorKind.Decrement, "op_Decrement", typeof(decimal), 1);
                return created;
            }
        }

        private static Captured Capture() => s_captured ??= new Captured();

        // Decimal's own MethodInfos for unary minus and its conversions, from the tree C# builds for this
        // lambda: each conversion and operator in Sink's arguments carries its method.
        private sealed class Captured
        {
            internal readonly MethodInfo Negate;
            internal readonly Dictionary<(TypeCode From, TypeCode To), MethodInfo> Conversions = new();
            internal readonly Dictionary<MethodInfo, DecimalOperatorKind> Kinds = new();

            internal Captured()
            {
                Expression<Func<byte, sbyte, short, ushort, char, int, uint, long, ulong, float, double, decimal, int>> sample =
                    (b, sb, s, us, c, i, ui, l, ul, f, d, m) => Sink(
                        b, sb, s, us, c, i, ui, l, ul, (decimal)f, (decimal)d,
                        (byte)m, (sbyte)m, (short)m, (ushort)m, (char)m, (int)m, (uint)m, (long)m, (ulong)m, (float)m, (double)m,
                        -m);
                MethodInfo? negate = null;
                foreach (var argument in ((MethodCallExpression)sample.Body).Arguments)
                {
                    if (argument is not UnaryExpression { Method: { } method } unary)
                    {
                        throw new InvalidOperationException("Decimal's operator methods are missing from the sample tree.");
                    }
                    if (unary.NodeType == ExpressionType.Negate)
                    {
                        negate = method;
                        Kinds[method] = DecimalOperatorKind.Negate;
                    }
                    else
                    {
                        Conversions[(Type.GetTypeCode(unary.Operand.Type), Type.GetTypeCode(unary.Type))] = method;
                        Kinds[method] = DecimalOperatorKind.Convert;
                    }
                }
                Negate = negate ?? throw new InvalidOperationException("Decimal's unary minus is missing from the sample tree.");
            }

            // Implicit conversions to decimal reach Sink's decimal parameters as Convert nodes too.
            private static int Sink(
                decimal fromByte, decimal fromSByte, decimal fromInt16, decimal fromUInt16, decimal fromChar, decimal fromInt32,
                decimal fromUInt32, decimal fromInt64, decimal fromUInt64, decimal fromSingle, decimal fromDouble,
                byte toByte, sbyte toSByte, short toInt16, ushort toUInt16, char toChar, int toInt32, uint toUInt32,
                long toInt64, ulong toUInt64, float toSingle, double toDouble, decimal negated) => 0;
        }
    }

    // One of decimal's operators that the factories supply without looking it up: public, static and special-named
    // as the operator is, with decimal parameters, and invoking it runs the operator.
    internal sealed class DecimalOperatorMethod : MethodInfo
    {
        private readonly string _name;
        private readonly Type _returnType;
        private readonly ParameterInfo[] _parameters;

        internal DecimalOperatorMethod(DecimalOperatorKind kind, string name, Type returnType, int arity)
        {
            Kind = kind;
            _name = name;
            _returnType = returnType;
            _parameters = arity == 1
                ? [new DecimalParameter(this, 0, "d")]
                : [new DecimalParameter(this, 0, "d1"), new DecimalParameter(this, 1, "d2")];
        }

        internal DecimalOperatorKind Kind { get; }

        public override string Name => _name;

        public override Type DeclaringType => typeof(decimal);

        public override Type ReflectedType => typeof(decimal);

        public override Type ReturnType => _returnType;

        public override MethodAttributes Attributes =>
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName;

        public override ParameterInfo[] GetParameters() => (ParameterInfo[])_parameters.Clone();

        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture)
        {
            if ((parameters?.Length ?? 0) != _parameters.Length)
            {
                throw new TargetParameterCountException();
            }
            foreach (var parameter in parameters!)
            {
                if (parameter is not decimal)
                {
                    throw new ArgumentException("The argument is not a decimal.", nameof(parameters));
                }
            }
            try
            {
                return _parameters.Length == 1
                    ? DecimalOperators.Apply(Kind, (decimal)parameters[0]!)
                    : DecimalOperators.Apply(Kind, (decimal)parameters[0]!, (decimal)parameters[1]!);
            }
            catch (Exception exception) when ((invokeAttr & BindingFlags.DoNotWrapExceptions) == 0)
            {
                throw new TargetInvocationException(exception);
            }
        }

        public override MethodInfo GetBaseDefinition() => this;

        public override string ToString() =>
            _returnType.ToString() + " " + _name + "(" + (_parameters.Length == 1 ? "System.Decimal" : "System.Decimal, System.Decimal") + ")";

        private sealed class DecimalParameter : ParameterInfo
        {
            internal DecimalParameter(MemberInfo member, int position, string name)
            {
                ClassImpl = typeof(decimal);
                MemberImpl = member;
                PositionImpl = position;
                NameImpl = name;
            }
        }
    }
}
