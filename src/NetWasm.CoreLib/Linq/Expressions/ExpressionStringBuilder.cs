// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text;

namespace System.Linq.Expressions
{
    internal sealed class ExpressionStringBuilder : ExpressionVisitor
    {
        private readonly StringBuilder _builder = new();
        private readonly Dictionary<ParameterExpression, int> _unnamed = new();

        private ExpressionStringBuilder()
        {
        }

        internal static string ExpressionToString(Expression node)
        {
            var builder = new ExpressionStringBuilder();
            builder.Visit(node);
            return builder._builder.ToString();
        }

        protected internal override Expression VisitBinary(BinaryExpression node)
        {
            _builder.Append('(');
            Visit(node.Left);
            _builder.Append(' ').Append(BinaryOperator(node.NodeType)).Append(' ');
            Visit(node.Right);
            _builder.Append(')');
            return node;
        }

        protected internal override Expression VisitBlock(BlockExpression node)
        {
            _builder.Append('{');
            for (var index = 0; index < node.Variables.Count; index++)
            {
                _builder.Append("var ");
                Visit(node.Variables[index]);
                _builder.Append(';');
            }
            _builder.Append(" ... }");
            return node;
        }

        protected internal override Expression VisitConditional(
            ConditionalExpression node)
        {
            _builder.Append("IIF(");
            Visit(node.Test);
            _builder.Append(", ");
            Visit(node.IfTrue);
            _builder.Append(", ");
            Visit(node.IfFalse);
            _builder.Append(')');
            return node;
        }

        protected internal override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is null)
            {
                _builder.Append("null");
            }
            else if (node.Value is string stringValue)
            {
                _builder.Append('"').Append(stringValue).Append('"');
            }
            else
            {
                string? display = node.Value.ToString();
                if (display == node.Value.GetType().ToString())
                {
                    _builder.Append("value(").Append(display).Append(')');
                }
                else
                {
                    _builder.Append(display);
                }
            }
            return node;
        }

        protected internal override Expression VisitDefault(DefaultExpression node)
        {
            _builder.Append("default(").Append(node.Type.Name).Append(')');
            return node;
        }

        protected internal override Expression VisitInvocation(
            InvocationExpression node)
        {
            _builder.Append("Invoke(");
            Visit(node.Expression);
            AppendArguments(node.Arguments, leadingComma: true);
            _builder.Append(')');
            return node;
        }

        protected internal override Expression VisitLambda<TDelegate>(
            Expression<TDelegate> node)
        {
            if (node.Parameters.Count != 1)
            {
                _builder.Append('(');
            }
            for (var index = 0; index < node.Parameters.Count; index++)
            {
                if (index != 0)
                {
                    _builder.Append(", ");
                }
                Visit(node.Parameters[index]);
            }
            if (node.Parameters.Count != 1)
            {
                _builder.Append(')');
            }
            _builder.Append(" => ");
            Visit(node.Body);
            return node;
        }

        protected internal override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is not null)
            {
                Visit(node.Expression);
            }
            else
            {
                _builder.Append(node.Member.DeclaringType!.Name);
            }
            _builder.Append('.').Append(node.Member.Name);
            return node;
        }

        protected internal override Expression VisitMethodCall(
            MethodCallExpression node)
        {
            if (node.Object is not null)
            {
                Visit(node.Object);
                _builder.Append('.');
            }
            _builder.Append(node.Method.Name).Append('(');
            AppendArguments(node.Arguments, leadingComma: false);
            _builder.Append(')');
            return node;
        }

        protected internal override Expression VisitNew(NewExpression node)
        {
            _builder.Append("new ")
                .Append(node.Constructor.DeclaringType!.Name)
                .Append('(');
            AppendArguments(node.Arguments, leadingComma: false);
            _builder.Append(')');
            return node;
        }

        protected internal override Expression VisitParameter(ParameterExpression node)
        {
            if (!string.IsNullOrEmpty(node.Name))
            {
                _builder.Append(node.Name);
            }
            else
            {
                if (!_unnamed.TryGetValue(node, out var index))
                {
                    index = _unnamed.Count;
                    _unnamed.Add(node, index);
                }
                _builder.Append("Param_").Append(index);
            }
            return node;
        }

        protected internal override Expression VisitUnary(UnaryExpression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Negate:
                    _builder.Append('-');
                    Visit(node.Operand);
                    break;
                case ExpressionType.Convert:
                    _builder.Append("Convert(");
                    Visit(node.Operand);
                    _builder.Append(", ").Append(node.Type.Name).Append(')');
                    break;
                case ExpressionType.Quote:
                    Visit(node.Operand);
                    break;
                default:
                    throw new NotSupportedException(
                        "This unary expression cannot be formatted by the NetWasm profile.");
            }
            return node;
        }

        private void AppendArguments(
            System.Collections.ObjectModel.ReadOnlyCollection<Expression> arguments,
            bool leadingComma)
        {
            for (var index = 0; index < arguments.Count; index++)
            {
                if (index != 0 || leadingComma)
                {
                    _builder.Append(", ");
                }
                Visit(arguments[index]);
            }
        }

        private static string BinaryOperator(ExpressionType nodeType) =>
            nodeType switch
            {
                ExpressionType.Add => "+",
                ExpressionType.AndAlso => "AndAlso",
                ExpressionType.Assign => "=",
                ExpressionType.GreaterThan => ">",
                ExpressionType.LessThan => "<",
                _ => throw new NotSupportedException(
                    "This binary expression cannot be formatted by the NetWasm profile."),
            };
    }
}
