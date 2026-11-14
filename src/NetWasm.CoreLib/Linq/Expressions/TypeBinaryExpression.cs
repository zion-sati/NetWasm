// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: ReduceTypeEqual and its helpers serve upstream's IL compiler, which NetWasm
// excludes, so they are not ported.
// NetWasm adaptation: upstream's debugger type proxy (its DebugView) is not ported.

using System.Diagnostics;
using System.Dynamic.Utils;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions
{
    /// <summary>
    /// Represents an operation between an expression and a type.
    /// </summary>
    public sealed class TypeBinaryExpression : Expression
    {
        internal TypeBinaryExpression(Expression expression, Type typeOperand, ExpressionType nodeType)
        {
            Expression = expression;
            TypeOperand = typeOperand;
            NodeType = nodeType;
        }

        /// <summary>
        /// Gets the static type of the expression that this <see cref="Expression"/> represents.
        /// </summary>
        /// <returns>The <see cref="System.Type"/> that represents the static type of the expression.</returns>
        public sealed override Type Type => typeof(bool);

        /// <summary>
        /// Returns the node type of this Expression. Extension nodes should return
        /// ExpressionType.Extension when overriding this method.
        /// </summary>
        /// <returns>The <see cref="ExpressionType"/> of the expression.</returns>
        public sealed override ExpressionType NodeType { get; }

        /// <summary>
        /// Gets the expression operand of a type test operation.
        /// </summary>
        public Expression Expression { get; }

        /// <summary>
        /// Gets the type operand of a type test operation.
        /// </summary>
        public Type TypeOperand { get; }

        /// <summary>
        /// Dispatches to the specific visit method for this node type.
        /// </summary>
        protected internal override Expression Accept(ExpressionVisitor visitor)
        {
            return visitor.VisitTypeBinary(this);
        }

        /// <summary>
        /// Creates a new expression that is like this one, but using the
        /// supplied children. If all of the children are the same, it will
        /// return this expression.
        /// </summary>
        /// <param name="expression">The <see cref="Expression"/> property of the result.</param>
        /// <returns>This expression if no children changed, or an expression with the updated children.</returns>
        public TypeBinaryExpression Update(Expression expression)
        {
            if (expression == Expression)
            {
                return this;
            }
            if (NodeType == ExpressionType.TypeIs)
            {
                return Expression.TypeIs(expression, TypeOperand);
            }
            return Expression.TypeEqual(expression, TypeOperand);
        }
    }

    public partial class Expression
    {
        /// <summary>
        /// Creates a <see cref="TypeBinaryExpression"/>.
        /// </summary>
        /// <param name="expression">An <see cref="Expression"/> to set the <see cref="Expression"/> property equal to.</param>
        /// <param name="type">A <see cref="Type"/> to set the <see cref="TypeBinaryExpression.TypeOperand"/> property equal to.</param>
        /// <returns>A <see cref="TypeBinaryExpression"/> for which the <see cref="NodeType"/> property is equal to <see cref="ExpressionType.TypeIs"/> and for which the <see cref="TypeBinaryExpression.Expression"/> and <see cref="TypeBinaryExpression.TypeOperand"/> properties are set to the specified values.</returns>
        public static TypeBinaryExpression TypeIs(Expression expression, Type type)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            ArgumentNullException.ThrowIfNull(type);
            if (type.IsByRef) throw Error.TypeMustNotBeByRef(nameof(type));

            return new TypeBinaryExpression(expression, type, ExpressionType.TypeIs);
        }

        /// <summary>
        /// Creates a <see cref="TypeBinaryExpression"/> that compares run-time type identity.
        /// </summary>
        /// <param name="expression">An <see cref="Expression"/> to set the <see cref="Expression"/> property equal to.</param>
        /// <param name="type">A <see cref="Type"/> to set the <see cref="TypeBinaryExpression.TypeOperand"/> property equal to.</param>
        /// <returns>A <see cref="TypeBinaryExpression"/> for which the <see cref="NodeType"/> property is equal to <see cref="ExpressionType.TypeEqual"/> and for which the <see cref="TypeBinaryExpression.Expression"/> and <see cref="TypeBinaryExpression.TypeOperand"/> properties are set to the specified values.</returns>
        public static TypeBinaryExpression TypeEqual(Expression expression, Type type)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            ArgumentNullException.ThrowIfNull(type);
            if (type.IsByRef) throw Error.TypeMustNotBeByRef(nameof(type));

            return new TypeBinaryExpression(expression, type, ExpressionType.TypeEqual);
        }
    }
}
