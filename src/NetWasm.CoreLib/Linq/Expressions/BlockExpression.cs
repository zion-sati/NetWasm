// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: one BlockExpression class stands in for upstream's Scope/ScopeN/ScopeWithType
// variants; a typed block whose type differs from its last expression's records that type. The typed
// Block factories and Update are upstream's; they validate variables with the earlier NetWasm helper.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic.Utils;

namespace System.Linq.Expressions
{
    public class BlockExpression : Expression
    {
        private readonly ReadOnlyCollection<ParameterExpression> _variables;
        private readonly ReadOnlyCollection<Expression> _expressions;
        private readonly Type? _type;

        internal BlockExpression(
            ParameterExpression[] variables,
            Expression[] expressions,
            Type? type = null)
        {
            _type = type;
            _variables = variables.Length == 0
                ? ReadOnlyCollection<ParameterExpression>.Empty
                : new ReadOnlyCollection<ParameterExpression>(variables);
            _expressions = expressions.Length == 0
                ? ReadOnlyCollection<Expression>.Empty
                : new ReadOnlyCollection<Expression>(expressions);
        }

        public sealed override ExpressionType NodeType => ExpressionType.Block;

        public override Type Type =>
            _type ?? (_expressions.Count == 0
                ? typeof(void)
                : _expressions[_expressions.Count - 1].Type);

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
                : Block(Type, variableArray, expressionArray);
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
        public static BlockExpression Block(Expression arg0, Expression arg1) =>
            Block(new[] { arg0, arg1 });

        public static BlockExpression Block(Expression arg0, Expression arg1, Expression arg2) =>
            Block(new[] { arg0, arg1, arg2 });

        public static BlockExpression Block(
            Expression arg0,
            Expression arg1,
            Expression arg2,
            Expression arg3) =>
            Block(new[] { arg0, arg1, arg2, arg3 });

        public static BlockExpression Block(
            Expression arg0,
            Expression arg1,
            Expression arg2,
            Expression arg3,
            Expression arg4) =>
            Block(new[] { arg0, arg1, arg2, arg3, arg4 });

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

        public static BlockExpression Block(Type type, params Expression[] expressions)
        {
            ArgumentNullException.ThrowIfNull(expressions);
            return Block(type, (IEnumerable<Expression>)expressions);
        }

        public static BlockExpression Block(Type type, IEnumerable<Expression> expressions)
        {
            return Block(type, ReadOnlyCollection<ParameterExpression>.Empty, expressions);
        }

        public static BlockExpression Block(Type type, IEnumerable<ParameterExpression>? variables, params Expression[] expressions)
        {
            return Block(type, variables, (IEnumerable<Expression>)expressions);
        }

        public static BlockExpression Block(Type type, IEnumerable<ParameterExpression>? variables, IEnumerable<Expression> expressions)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(expressions);

            Expression[] expressionList = ExpressionValidation.CopyExpressions(expressions, nameof(expressions));
            for (int i = 0; i < expressionList.Length; i++)
            {
                ExpressionUtils.RequiresCanRead(expressionList[i], nameof(expressions), i);
            }

            // NetWasm: the earlier block helper validates the variables (upstream's ValidateVariables).
            ParameterExpression[] variableList = CopyBlockVariables(variables);

            if (variableList.Length == 0 && expressionList.Length != 0)
            {
                int expressionCount = expressionList.Length;

                if (expressionCount != 0)
                {
                    Expression lastExpression = expressionList[expressionCount - 1];

                    if (lastExpression.Type == type)
                    {
                        return new BlockExpression(variableList, expressionList);
                    }
                }
            }

            return BlockCore(type, variableList, expressionList);
        }

        private static BlockExpression BlockCore(Type? type, ParameterExpression[] variables, Expression[] expressions)
        {
            if (type != null)
            {
                if (expressions.Length == 0)
                {
                    if (type != typeof(void))
                    {
                        throw Error.ArgumentTypesMustMatch();
                    }

                    return new BlockExpression(variables, expressions, type);
                }
                Expression last = expressions[^1];
                if (type != typeof(void))
                {
                    if (!TypeUtils.AreReferenceAssignable(type, last.Type))
                    {
                        throw Error.ArgumentTypesMustMatch();
                    }
                }

                if (!TypeUtils.AreEquivalent(type, last.Type))
                {
                    return new BlockExpression(variables, expressions, type);
                }
            }

            return new BlockExpression(variables, expressions);
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
