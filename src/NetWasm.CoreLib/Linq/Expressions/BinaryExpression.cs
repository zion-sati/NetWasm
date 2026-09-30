// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System.Linq.Expressions
{
    public class BinaryExpression : Expression
    {
        private readonly ExpressionType _nodeType;
        private readonly Type _type;

        internal BinaryExpression(
            ExpressionType nodeType,
            Expression left,
            Expression right,
            Type type,
            MethodInfo? method)
        {
            _nodeType = nodeType;
            Left = left;
            Right = right;
            _type = type;
            Method = method;
        }

        public override ExpressionType NodeType => _nodeType;

        public override Type Type => _type;

        public Expression Left { get; }

        public Expression Right { get; }

        public MethodInfo? Method { get; }

        public LambdaExpression? Conversion => null;

        public bool IsLifted => false;

        public bool IsLiftedToNull => false;

        public BinaryExpression Update(
            Expression left,
            LambdaExpression? conversion,
            Expression right)
        {
            if (conversion is not null)
            {
                throw new NotSupportedException(
                    "Coalescing conversions are outside the NetWasm expression profile.");
            }
            return ReferenceEquals(left, Left) && ReferenceEquals(right, Right)
                ? this
                : MakeBinary(_nodeType, left, right, Method);
        }

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitBinary(this);
    }

    public abstract partial class Expression
    {
        public static BinaryExpression Add(Expression left, Expression right) =>
            MakeBinary(ExpressionType.Add, left, right, method: null);

        public static BinaryExpression Add(
            Expression left,
            Expression right,
            MethodInfo? method) =>
            MakeBinary(ExpressionType.Add, left, right, method);

        public static BinaryExpression GreaterThan(
            Expression left,
            Expression right) =>
            MakeBinary(ExpressionType.GreaterThan, left, right, method: null);

        public static BinaryExpression GreaterThan(
            Expression left,
            Expression right,
            bool liftToNull,
            MethodInfo? method)
        {
            if (liftToNull)
            {
                throw new NotSupportedException(
                    "Lifted operators are outside the NetWasm expression profile.");
            }
            return MakeBinary(ExpressionType.GreaterThan, left, right, method);
        }

        public static BinaryExpression LessThan(
            Expression left,
            Expression right) =>
            MakeBinary(ExpressionType.LessThan, left, right, method: null);

        public static BinaryExpression LessThan(
            Expression left,
            Expression right,
            bool liftToNull,
            MethodInfo? method)
        {
            if (liftToNull)
            {
                throw new NotSupportedException(
                    "Lifted operators are outside the NetWasm expression profile.");
            }
            return MakeBinary(ExpressionType.LessThan, left, right, method);
        }

        public static BinaryExpression AndAlso(
            Expression left,
            Expression right) =>
            MakeBinary(ExpressionType.AndAlso, left, right, method: null);

        public static BinaryExpression AndAlso(
            Expression left,
            Expression right,
            MethodInfo? method) =>
            MakeBinary(ExpressionType.AndAlso, left, right, method);

        public static BinaryExpression Assign(Expression left, Expression right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            ValidateWriteable(left);
            ExpressionValidation.RequiresCanRead(right, nameof(right));
            if (!ExpressionValidation.AreReferenceAssignable(
                left.Type,
                right.Type))
            {
                throw new ArgumentException(
                    "The assignment value does not match the target type.",
                    nameof(right));
            }
            return new BinaryExpression(
                ExpressionType.Assign,
                left,
                right,
                left.Type,
                method: null);
        }

        internal static BinaryExpression MakeBinary(
            ExpressionType nodeType,
            Expression left,
            Expression right,
            MethodInfo? method)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            if (nodeType == ExpressionType.Assign)
            {
                return Assign(left, right);
            }
            if (method is not null)
            {
                if (nodeType == ExpressionType.AndAlso)
                {
                    throw new NotSupportedException(
                        "Method-backed conditional Boolean operators are not implemented.");
                }
                ExpressionValidation.RequiresCanRead(left, nameof(left));
                ExpressionValidation.RequiresCanRead(right, nameof(right));
                ValidateOperatorMethod(method, [left, right]);
                Type resultType = nodeType is
                    ExpressionType.GreaterThan or ExpressionType.LessThan
                        ? typeof(bool)
                        : method.ReturnType;
                if (nodeType is ExpressionType.GreaterThan or ExpressionType.LessThan &&
                    method.ReturnType != typeof(bool))
                {
                    throw new ArgumentException(
                        "A comparison operator must return bool.",
                        nameof(method));
                }
                return new BinaryExpression(
                    nodeType,
                    left,
                    right,
                    resultType,
                    method);
            }
            if (left.Type != right.Type)
            {
                throw new ArgumentException(
                    "Binary operands must have the same type.");
            }
            ExpressionValidation.RequiresCanRead(left, nameof(left));
            ExpressionValidation.RequiresCanRead(right, nameof(right));
            if (nodeType == ExpressionType.AndAlso)
            {
                if (left.Type != typeof(bool))
                {
                    throw new ArgumentException(
                        "AndAlso requires Boolean operands.");
                }
                return new BinaryExpression(
                    nodeType,
                    left,
                    right,
                    typeof(bool),
                    method: null);
            }
            if (!IsArithmetic(left.Type))
            {
                throw new NotSupportedException(
                    "Only built-in numeric operators are implemented without an explicit method.");
            }
            Type type = nodeType is
                ExpressionType.GreaterThan or ExpressionType.LessThan
                    ? typeof(bool)
                    : left.Type;
            return new BinaryExpression(nodeType, left, right, type, method: null);
        }

        internal static void ValidateOperatorMethod(
            MethodInfo method,
            Expression[] operands)
        {
            if (!method.IsStatic || method.ContainsGenericParameters)
            {
                throw new ArgumentException(
                    "An operator method must be a closed static method.",
                    nameof(method));
            }
            ExpressionValidation.ValidateArguments(
                method,
                operands,
                nameof(method));
            if (method.ReturnType == typeof(void))
            {
                throw new ArgumentException(
                    "An operator method must return a value.",
                    nameof(method));
            }
        }

        internal static bool IsNumeric(Type type)
        {
            if (type.IsEnum)
            {
                return false;
            }
            return Type.GetTypeCode(type) is
                TypeCode.SByte or TypeCode.Byte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
        }

        internal static bool IsArithmetic(Type type)
        {
            if (type.IsEnum)
            {
                return false;
            }
            return Type.GetTypeCode(type) is
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double;
        }

        internal static bool IsSignedArithmetic(Type type) =>
            !type.IsEnum &&
            Type.GetTypeCode(type) is
                TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
                TypeCode.Single or TypeCode.Double;

        private static void ValidateWriteable(Expression expression)
        {
            if (expression is ParameterExpression)
            {
                return;
            }
            if (expression is MemberExpression member)
            {
                if (member.Member is FieldInfo field &&
                    !field.IsInitOnly && !field.IsLiteral)
                {
                    return;
                }
                if (member.Member is PropertyInfo property && property.CanWrite)
                {
                    return;
                }
            }
            throw new ArgumentException(
                "The left expression is not writeable.",
                nameof(expression));
        }
    }
}
