// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: AstUtils is not ported, so the empty IfFalse singleton is a private field.

using System.Dynamic.Utils;

namespace System.Linq.Expressions
{
    public class ConditionalExpression : Expression
    {
        private static readonly DefaultExpression s_empty = new DefaultExpression(typeof(void));

        internal ConditionalExpression(Expression test, Expression ifTrue)
        {
            Test = test;
            IfTrue = ifTrue;
        }

        internal static ConditionalExpression Make(Expression test, Expression ifTrue, Expression ifFalse, Type type)
        {
            if (ifTrue.Type != type || ifFalse.Type != type)
            {
                return new FullConditionalExpressionWithType(test, ifTrue, ifFalse, type);
            }
            if (ifFalse is DefaultExpression && ifFalse.Type == typeof(void))
            {
                return new ConditionalExpression(test, ifTrue);
            }
            else
            {
                return new FullConditionalExpression(test, ifTrue, ifFalse);
            }
        }

        public sealed override ExpressionType NodeType => ExpressionType.Conditional;

        public override Type Type => IfTrue.Type;

        public Expression Test { get; }

        public Expression IfTrue { get; }

        public Expression IfFalse => GetFalse();

        internal virtual Expression GetFalse()
        {
            // Using a singleton here to ensure a stable object identity for IfFalse, which Update relies on.
            // NetWasm: AstUtils is not ported, so the singleton lives here.
            return s_empty;
        }

        protected internal override Expression Accept(ExpressionVisitor visitor)
        {
            return visitor.VisitConditional(this);
        }

        public ConditionalExpression Update(Expression test, Expression ifTrue, Expression ifFalse)
        {
            if (test == Test && ifTrue == IfTrue && ifFalse == IfFalse)
            {
                return this;
            }
            return Expression.Condition(test, ifTrue, ifFalse, Type);
        }
    }

    internal class FullConditionalExpression : ConditionalExpression
    {
        private readonly Expression _false;

        internal FullConditionalExpression(Expression test, Expression ifTrue, Expression ifFalse)
            : base(test, ifTrue)
        {
            _false = ifFalse;
        }

        internal override Expression GetFalse() => _false;
    }

    internal sealed class FullConditionalExpressionWithType : FullConditionalExpression
    {
        internal FullConditionalExpressionWithType(Expression test, Expression ifTrue, Expression ifFalse, Type type)
            : base(test, ifTrue, ifFalse)
        {
            Type = type;
        }

        public sealed override Type Type { get; }
    }

    public abstract partial class Expression
    {
        public static ConditionalExpression Condition(Expression test, Expression ifTrue, Expression ifFalse)
        {
            ExpressionUtils.RequiresCanRead(test, nameof(test));
            ExpressionUtils.RequiresCanRead(ifTrue, nameof(ifTrue));
            ExpressionUtils.RequiresCanRead(ifFalse, nameof(ifFalse));

            if (test.Type != typeof(bool))
            {
                throw Error.ArgumentMustBeBoolean(nameof(test));
            }
            if (!TypeUtils.AreEquivalent(ifTrue.Type, ifFalse.Type))
            {
                throw Error.ArgumentTypesMustMatch();
            }

            return ConditionalExpression.Make(test, ifTrue, ifFalse, ifTrue.Type);
        }

        public static ConditionalExpression Condition(Expression test, Expression ifTrue, Expression ifFalse, Type type)
        {
            ExpressionUtils.RequiresCanRead(test, nameof(test));
            ExpressionUtils.RequiresCanRead(ifTrue, nameof(ifTrue));
            ExpressionUtils.RequiresCanRead(ifFalse, nameof(ifFalse));
            ArgumentNullException.ThrowIfNull(type);

            if (test.Type != typeof(bool))
            {
                throw Error.ArgumentMustBeBoolean(nameof(test));
            }

            if (type != typeof(void))
            {
                if (!TypeUtils.AreReferenceAssignable(type, ifTrue.Type) ||
                    !TypeUtils.AreReferenceAssignable(type, ifFalse.Type))
                {
                    throw Error.ArgumentTypesMustMatch();
                }
            }

            return ConditionalExpression.Make(test, ifTrue, ifFalse, type);
        }

        public static ConditionalExpression IfThen(Expression test, Expression ifTrue)
        {
            return Condition(test, ifTrue, Expression.Empty(), typeof(void));
        }

        public static ConditionalExpression IfThenElse(Expression test, Expression ifTrue, Expression ifFalse)
        {
            return Condition(test, ifTrue, ifFalse, typeof(void));
        }
    }
}
