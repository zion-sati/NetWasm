// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions
{
    public abstract partial class Expression
    {
        protected Expression()
        {
        }

        public virtual ExpressionType NodeType =>
            throw new InvalidOperationException(
                "An extension expression must override NodeType.");

        public virtual Type Type =>
            throw new InvalidOperationException(
                "An extension expression must override Type.");

        public virtual bool CanReduce => false;

        public virtual Expression Reduce()
        {
            if (CanReduce)
            {
                throw new InvalidOperationException(
                    "A reducible expression must override Reduce.");
            }
            return this;
        }

        public Expression ReduceAndCheck()
        {
            if (!CanReduce)
            {
                throw new InvalidOperationException(
                    "The expression is not reducible.");
            }
            Expression reduced = Reduce();
            if (reduced is null || ReferenceEquals(reduced, this))
            {
                throw new InvalidOperationException(
                    "Reduce must return a different non-null expression.");
            }
            if (!ExpressionValidation.AreReferenceAssignable(
                Type,
                reduced.Type))
            {
                throw new InvalidOperationException(
                    "The reduced expression has an incompatible type.");
            }
            return reduced;
        }

        public Expression ReduceExtensions()
        {
            Expression expression = this;
            while (expression.NodeType == ExpressionType.Extension)
            {
                expression = expression.ReduceAndCheck();
            }
            return expression;
        }

        protected internal virtual Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitExtension(this);

        protected internal virtual Expression VisitChildren(
            ExpressionVisitor visitor)
        {
            if (!CanReduce)
            {
                throw new ArgumentException(
                    "An extension expression must be reducible to visit its children.",
                    nameof(visitor));
            }
            return visitor.Visit(ReduceAndCheck())!;
        }

        public override string ToString() =>
            ExpressionStringBuilder.ExpressionToString(this);
    }
}
