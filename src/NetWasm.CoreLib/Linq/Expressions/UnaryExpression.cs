// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System.Linq.Expressions
{
    public sealed class UnaryExpression : Expression
    {
        private readonly ExpressionType _nodeType;
        private readonly Type _type;

        internal UnaryExpression(
            ExpressionType nodeType,
            Expression operand,
            Type type,
            MethodInfo? method)
        {
            _nodeType = nodeType;
            Operand = operand;
            _type = type;
            Method = method;
        }

        public override ExpressionType NodeType => _nodeType;

        public override Type Type => _type;

        public Expression Operand { get; }

        public MethodInfo? Method { get; }

        public bool IsLifted =>
            _nodeType == ExpressionType.Convert && Method is null &&
            (Operand.Type.IsNullable || Type.IsNullable);

        public bool IsLiftedToNull => IsLifted && Type.IsNullable;

        public UnaryExpression Update(Expression operand) =>
            ReferenceEquals(operand, Operand)
                ? this
                : _nodeType switch
                {
                    ExpressionType.Negate => Negate(operand, Method),
                    ExpressionType.Convert => Convert(operand, _type, Method),
                    ExpressionType.Quote => Quote(operand),
                    _ => throw new NotSupportedException(
                        "This unary expression shape is outside the NetWasm profile."),
                };

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitUnary(this);
    }

    public abstract partial class Expression
    {
        public static UnaryExpression Negate(Expression expression) =>
            Negate(expression, method: null);

        public static UnaryExpression Negate(
            Expression expression,
            MethodInfo? method)
        {
            ArgumentNullException.ThrowIfNull(expression);
            ExpressionValidation.RequiresCanRead(
                expression,
                nameof(expression));
            Type resultType;
            if (method is null)
            {
                if (!IsSignedArithmetic(expression.Type))
                {
                    throw new NotSupportedException(
                        "Only built-in numeric negation is implemented without an explicit method.");
                }
                resultType = expression.Type;
            }
            else
            {
                ValidateOperatorMethod(method, [expression]);
                resultType = method.ReturnType;
            }
            return new UnaryExpression(
                ExpressionType.Negate,
                expression,
                resultType,
                method);
        }

        public static UnaryExpression Convert(Expression expression, Type type) =>
            Convert(expression, type, method: null);

        public static UnaryExpression Convert(
            Expression expression,
            Type type,
            MethodInfo? method)
        {
            ArgumentNullException.ThrowIfNull(expression);
            ArgumentNullException.ThrowIfNull(type);
            ExpressionValidation.RequiresCanRead(
                expression,
                nameof(expression));
            ExpressionValidation.ValidateType(
                type,
                nameof(type),
                allowVoid: false);
            if (type == typeof(void) || type.IsByRef || type.IsPointer)
            {
                throw new NotSupportedException(
                    "This conversion target is outside the NetWasm expression profile.");
            }
            if (method is not null)
            {
                ValidateOperatorMethod(method, [expression]);
                if (method.ReturnType != type)
                {
                    throw new ArgumentException(
                        "The conversion method return type does not match the target type.",
                        nameof(method));
                }
            }
            else if (expression.Type != type &&
                !type.IsAssignableFrom(expression.Type) &&
                !expression.Type.IsAssignableFrom(type) &&
                !HasBuiltInNumericOrNullableConversion(expression.Type, type))
            {
                throw new NotSupportedException(
                    "The requested built-in conversion is outside the NetWasm expression profile.");
            }
            return new UnaryExpression(
                ExpressionType.Convert,
                expression,
                type,
                method);
        }

        private static bool HasBuiltInNumericOrNullableConversion(Type source, Type target)
        {
            Type from = Nullable.GetUnderlyingType(source) ?? source;
            Type to = Nullable.GetUnderlyingType(target) ?? target;
            return from == to ||
                IsNumeric(from) && IsNumeric(to) &&
                from != typeof(decimal) && to != typeof(decimal);
        }

        public static UnaryExpression Quote(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            if (expression is not LambdaExpression)
            {
                throw new ArgumentException(
                    "Only lambda expressions can be quoted.",
                    nameof(expression));
            }
            return new UnaryExpression(
                ExpressionType.Quote,
                expression,
                ((LambdaExpression)expression).PublicType,
                method: null);
        }
    }
}
