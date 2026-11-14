// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm addition. Upstream's interpreter boxes a variable when a nested lambda first reaches it and rewrites the
// instructions already emitted for it (LocalVariables.Box, InstructionList.SwitchToBoxed). NetWasm finds the captured
// variables before compiling instead, so each is boxed from its definition and no instruction is rewritten.

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace System.Linq.Expressions.Interpreter
{
    // Which variables nested lambdas and quotes capture. A nested lambda or quote captures its free variables: the
    // parameters it reads or writes that no scope inside it defines. An Invoke of a lambda (or of a quoted lambda)
    // compiles in place, so its body belongs to the enclosing lambda rather than nesting in it.
    internal static class ClosureVariableFinder
    {
        // The variables the lambda's nested lambdas and quotes capture, which the lambda boxes where it defines them.
        internal static HashSet<ParameterExpression> Captured(LambdaExpression lambda)
        {
            var finder = new CapturedVariables();
            finder.Visit(lambda.Body);
            return finder.Variables;
        }

        // The variables a nested lambda (or a quoted expression) reads or writes without defining them, in the order
        // it first names them, which is the order of its closure.
        internal static List<ParameterExpression> Free(Expression expression)
        {
            var finder = new FreeVariables();
            finder.Visit(expression);
            return finder.Variables;
        }

        internal static LambdaExpression? InlinedLambda(InvocationExpression node) => node.Expression switch
        {
            LambdaExpression lambda => lambda,
            UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted } => quoted,
            _ => null,
        };

        private sealed class CapturedVariables : ExpressionVisitor
        {
            internal HashSet<ParameterExpression> Variables { get; } = new();

            protected internal override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
            {
                Variables.UnionWith(Free(node));
                return node;
            }

            protected internal override Expression VisitUnary(UnaryExpression node)
            {
                if (node.NodeType == ExpressionType.Quote)
                {
                    Variables.UnionWith(Free(node.Operand));
                    return node;
                }
                return base.VisitUnary(node);
            }

            protected internal override Expression VisitInvocation(InvocationExpression node)
            {
                if (InlinedLambda(node) is not { } lambda)
                {
                    return base.VisitInvocation(node);
                }
                Visit(lambda.Body);
                Visit(node.Arguments);
                return node;
            }
        }

        private sealed class FreeVariables : ExpressionVisitor
        {
            private readonly List<IReadOnlyCollection<ParameterExpression>> _scopes = new();
            private readonly HashSet<ParameterExpression> _seen = new();

            internal List<ParameterExpression> Variables { get; } = new();

            protected internal override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
            {
                Scoped(node.Parameters, node.Body);
                return node;
            }

            protected internal override Expression VisitBlock(BlockExpression node)
            {
                _scopes.Add(node.Variables);
                try
                {
                    Visit(node.Expressions);
                }
                finally
                {
                    _scopes.RemoveAt(_scopes.Count - 1);
                }
                return node;
            }

            protected override CatchBlock VisitCatchBlock(CatchBlock node)
            {
                _scopes.Add(node.Variable is null ? Array.Empty<ParameterExpression>() : new[] { node.Variable });
                try
                {
                    Visit(node.Filter);
                    Visit(node.Body);
                }
                finally
                {
                    _scopes.RemoveAt(_scopes.Count - 1);
                }
                return node;
            }

            protected internal override Expression VisitParameter(ParameterExpression node)
            {
                foreach (var scope in _scopes)
                {
                    foreach (var defined in scope)
                    {
                        if (defined == node)
                        {
                            return node;
                        }
                    }
                }
                if (_seen.Add(node))
                {
                    Variables.Add(node);
                }
                return node;
            }

            private void Scoped(ReadOnlyCollection<ParameterExpression> variables, Expression body)
            {
                _scopes.Add(variables);
                try
                {
                    Visit(body);
                }
                finally
                {
                    _scopes.RemoveAt(_scopes.Count - 1);
                }
            }
        }
    }
}
