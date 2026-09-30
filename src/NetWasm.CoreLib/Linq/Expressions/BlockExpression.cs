// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace System.Linq.Expressions
{
    public class BlockExpression : Expression
    {
        private readonly ReadOnlyCollection<ParameterExpression> _variables;
        private readonly ReadOnlyCollection<Expression> _expressions;

        internal BlockExpression(
            ParameterExpression[] variables,
            Expression[] expressions)
        {
            _variables = variables.Length == 0
                ? ReadOnlyCollection<ParameterExpression>.Empty
                : new ReadOnlyCollection<ParameterExpression>(variables);
            _expressions = expressions.Length == 0
                ? ReadOnlyCollection<Expression>.Empty
                : new ReadOnlyCollection<Expression>(expressions);
        }

        public sealed override ExpressionType NodeType => ExpressionType.Block;

        public override Type Type =>
            _expressions.Count == 0
                ? typeof(void)
                : _expressions[_expressions.Count - 1].Type;

        public ReadOnlyCollection<ParameterExpression> Variables => _variables;

        public ReadOnlyCollection<Expression> Expressions => _expressions;

        public Expression Result => _expressions[_expressions.Count - 1];

        public BlockExpression Update(
            IEnumerable<ParameterExpression>? variables,
            IEnumerable<Expression> expressions)
        {
            ArgumentNullException.ThrowIfNull(expressions);
            ParameterExpression[] variableArray =
                Expression.CopyBlockVariables(variables);
            Expression[] expressionArray = ExpressionValidation.CopyExpressions(
                expressions,
                nameof(expressions));
            return SameVariables(variableArray) &&
                ExpressionValidation.SameExpressions(
                    _expressions,
                    expressionArray)
                ? this
                : Block(variableArray, expressionArray);
        }

        private bool SameVariables(IEnumerable<ParameterExpression>? variables)
        {
            if (variables is null)
            {
                return _variables.Count == 0;
            }
            using IEnumerator<ParameterExpression> enumerator = variables.GetEnumerator();
            for (var index = 0; index < _variables.Count; index++)
            {
                if (!enumerator.MoveNext() ||
                    !ReferenceEquals(enumerator.Current, _variables[index]))
                {
                    return false;
                }
            }
            return !enumerator.MoveNext();
        }

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitBlock(this);
    }

    public abstract partial class Expression
    {
        public static BlockExpression Block(params Expression[] expressions) =>
            Block(variables: null, (IEnumerable<Expression>)expressions);

        public static BlockExpression Block(
            IEnumerable<Expression> expressions) =>
            Block(variables: null, expressions);

        public static BlockExpression Block(
            IEnumerable<ParameterExpression>? variables,
            params Expression[] expressions) =>
            Block(variables, (IEnumerable<Expression>)expressions);

        public static BlockExpression Block(
            IEnumerable<ParameterExpression>? variables,
            IEnumerable<Expression> expressions)
        {
            ArgumentNullException.ThrowIfNull(expressions);
            ParameterExpression[] variableArray = CopyBlockVariables(variables);
            Expression[] expressionArray = ExpressionValidation.CopyExpressions(
                expressions,
                nameof(expressions));
            for (var index = 0; index < expressionArray.Length; index++)
            {
                ExpressionValidation.RequiresCanRead(
                    expressionArray[index],
                    nameof(expressions));
            }
            return new BlockExpression(variableArray, expressionArray);
        }

        internal static ParameterExpression[] CopyBlockVariables(
            IEnumerable<ParameterExpression>? variables)
        {
            if (variables is null)
            {
                return [];
            }
            var copy = new List<ParameterExpression>();
            foreach (ParameterExpression? variable in variables)
            {
                if (variable is null)
                {
                    throw new ArgumentNullException(nameof(variables));
                }
                if (variable.IsByRef)
                {
                    throw new NotSupportedException(
                        "By-reference block variables are not implemented.");
                }
                for (var index = 0; index < copy.Count; index++)
                {
                    if (ReferenceEquals(copy[index], variable))
                    {
                        throw new ArgumentException(
                            "Block variables must be unique.",
                            nameof(variables));
                    }
                }
                copy.Add(variable);
            }
            return copy.ToArray();
        }
    }
}
