// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions
{
    public class ConditionalExpression : Expression
    {
        internal ConditionalExpression(
            Expression test,
            Expression ifTrue,
            Expression ifFalse)
        {
            Test = test;
            IfTrue = ifTrue;
            IfFalse = ifFalse;
        }

        public sealed override ExpressionType NodeType => ExpressionType.Conditional;

        public override Type Type => IfTrue.Type;

        public Expression Test { get; }

        public Expression IfTrue { get; }

        public Expression IfFalse { get; }

        public ConditionalExpression Update(
            Expression test,
            Expression ifTrue,
            Expression ifFalse) =>
            ReferenceEquals(test, Test) &&
                ReferenceEquals(ifTrue, IfTrue) &&
                ReferenceEquals(ifFalse, IfFalse)
                    ? this
                    : Condition(test, ifTrue, ifFalse);

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitConditional(this);
    }

    public abstract partial class Expression
    {
        public static ConditionalExpression Condition(
            Expression test,
            Expression ifTrue,
            Expression ifFalse)
        {
            ArgumentNullException.ThrowIfNull(test);
            ArgumentNullException.ThrowIfNull(ifTrue);
            ArgumentNullException.ThrowIfNull(ifFalse);
            ExpressionValidation.RequiresCanRead(test, nameof(test));
            ExpressionValidation.RequiresCanRead(ifTrue, nameof(ifTrue));
            ExpressionValidation.RequiresCanRead(ifFalse, nameof(ifFalse));
            if (test.Type != typeof(bool))
            {
                throw new ArgumentException(
                    "A conditional test must have type bool.",
                    nameof(test));
            }
            if (ifTrue.Type != ifFalse.Type)
            {
                throw new ArgumentException(
                    "Conditional branches must have the same type.");
            }
            return new ConditionalExpression(test, ifTrue, ifFalse);
        }
    }
}
