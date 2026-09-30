// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions
{
    public class ConstantExpression : Expression
    {
        private readonly Type _type;

        internal ConstantExpression(object? value, Type type)
        {
            Value = value;
            _type = type;
        }

        public sealed override ExpressionType NodeType => ExpressionType.Constant;

        public override Type Type => _type;

        public object? Value { get; }

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitConstant(this);
    }

    public abstract partial class Expression
    {
        public static ConstantExpression Constant(object? value) =>
            new(value, value?.GetType() ?? typeof(object));

        public static ConstantExpression Constant(object? value, Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            ExpressionValidation.ValidateType(
                type,
                nameof(type),
                allowVoid: false);
            if (type == typeof(void))
            {
                throw new ArgumentException(
                    "A constant cannot have type void.",
                    nameof(type));
            }
            if (type.IsByRef || type.IsPointer)
            {
                throw new NotSupportedException(
                    "By-reference and pointer constants are outside the NetWasm profile.");
            }
            if (value is null)
            {
                if (type.IsValueType && !type.IsNullable)
                {
                    throw new ArgumentException(
                        "A null value cannot be assigned to this value type.",
                        nameof(value));
                }
                return new ConstantExpression(null, type);
            }
            if (type.IsNullable)
            {
                throw new NotSupportedException(
                    "Non-null nullable constants are outside the NetWasm profile.");
            }
            Type valueType = value.GetType();
            if (type != valueType && !type.IsAssignableFrom(valueType))
            {
                throw new ArgumentException(
                    "The value does not match the requested constant type.",
                    nameof(value));
            }
            return new ConstantExpression(value, type);
        }
    }
}
