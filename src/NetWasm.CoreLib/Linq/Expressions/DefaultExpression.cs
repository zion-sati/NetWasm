// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions
{
    public sealed class DefaultExpression : Expression
    {
        internal DefaultExpression(Type type)
        {
            Type = type;
        }

        public sealed override ExpressionType NodeType => ExpressionType.Default;

        public override Type Type { get; }

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitDefault(this);
    }

    public abstract partial class Expression
    {
        public static DefaultExpression Default(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            ExpressionValidation.ValidateType(
                type,
                nameof(type),
                allowVoid: true);
            if (type.IsByRef || type.IsPointer)
            {
                throw new NotSupportedException(
                    "This default-expression type is outside the NetWasm profile.");
            }
            return new DefaultExpression(type);
        }

        public static DefaultExpression Empty() => Default(typeof(void));
    }
}
