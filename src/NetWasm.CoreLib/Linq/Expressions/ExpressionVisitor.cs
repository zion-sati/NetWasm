// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic.Utils;

namespace System.Linq.Expressions
{
    public abstract partial class ExpressionVisitor
    {
        protected ExpressionVisitor()
        {
        }

        [return: NotNullIfNotNull(nameof(node))]
        public virtual Expression? Visit(Expression? node) => node?.Accept(this);

        public ReadOnlyCollection<Expression> Visit(
            ReadOnlyCollection<Expression> nodes) =>
            Visit(nodes, node => Visit(node)!);

        public static ReadOnlyCollection<T> Visit<T>(
            ReadOnlyCollection<T> nodes,
            Func<T, T> elementVisitor)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(elementVisitor);
            T[]? replacements = null;
            for (var index = 0; index < nodes.Count; index++)
            {
                T replacement = elementVisitor(nodes[index]);
                if (replacements is not null)
                {
                    replacements[index] = replacement;
                }
                else if (!ReferenceEquals(replacement, nodes[index]))
                {
                    replacements = new T[nodes.Count];
                    for (var previous = 0; previous < index; previous++)
                    {
                        replacements[previous] = nodes[previous];
                    }
                    replacements[index] = replacement;
                }
            }
            return replacements is null
                ? nodes
                : new ReadOnlyCollection<T>(replacements);
        }

        [return: NotNullIfNotNull(nameof(node))]
        public T? VisitAndConvert<T>(T? node, string? callerName)
            where T : Expression
        {
            if (node is null)
            {
                return null;
            }
            if (Visit(node) is not T replacement)
            {
                throw new InvalidOperationException(
                    "A visitor must rewrite this node to the same expression type.");
            }
            return replacement;
        }

        public ReadOnlyCollection<T> VisitAndConvert<T>(
            ReadOnlyCollection<T> nodes,
            string? callerName)
            where T : Expression =>
            Visit(nodes, node => VisitAndConvert(node, callerName) ??
                throw new InvalidOperationException(
                    "A visitor cannot replace a required expression with null."));

        protected internal virtual Expression VisitBinary(BinaryExpression node)
        {
            Expression? left = Visit(node.Left);
            LambdaExpression? conversion = VisitAndConvert(
                node.Conversion,
                nameof(VisitBinary));
            Expression? right = Visit(node.Right);
            BinaryExpression replacement = node.Update(
                left!,
                conversion,
                right!);
            if (replacement != node && node.Method is null)
            {
                if (replacement.Method is not null)
                {
                    throw new InvalidOperationException(
                        "A visitor cannot add an operator method while rewriting a built-in binary expression.");
                }
                ValidateChildType(node.Left.Type, replacement.Left.Type);
                ValidateChildType(node.Right.Type, replacement.Right.Type);
            }
            return replacement;
        }

        protected internal virtual Expression VisitBlock(BlockExpression node)
        {
            ReadOnlyCollection<Expression> expressions = Visit(node.Expressions);
            ReadOnlyCollection<ParameterExpression> variables = VisitAndConvert(
                node.Variables,
                nameof(VisitBlock));
            return node.Update(variables, expressions);
        }

        protected internal virtual Expression VisitConditional(
            ConditionalExpression node) =>
            node.Update(
                Visit(node.Test)!,
                Visit(node.IfTrue)!,
                Visit(node.IfFalse)!);

        protected internal virtual Expression VisitConstant(ConstantExpression node) =>
            node;

        protected internal virtual Expression VisitDefault(DefaultExpression node) =>
            node;

        protected internal virtual Expression VisitExtension(Expression node) =>
            node.VisitChildren(this);

        protected internal virtual Expression VisitIndex(IndexExpression node)
        {
            Expression o = Visit(node.Object)!;
            Expression[]? a = ExpressionVisitorUtils.VisitArguments(this, node);
            if (o == node.Object && a == null)
            {
                return node;
            }

            return node.Rewrite(o, a);
        }

        protected internal virtual Expression VisitInvocation(
            InvocationExpression node) =>
            node.Update(
                Visit(node.Expression)!,
                Visit(node.Arguments));

        protected internal virtual Expression VisitLambda<TDelegate>(
            Expression<TDelegate> node) =>
            node.Update(
                Visit(node.Body)!,
                VisitAndConvert(node.Parameters, nameof(VisitLambda)));

        protected internal virtual Expression VisitListInit(ListInitExpression node)
        {
            return node.Update(
                VisitAndConvert(node.NewExpression, nameof(VisitListInit)),
                Visit(node.Initializers, VisitElementInit)
            );
        }

        protected internal virtual Expression VisitMember(MemberExpression node) =>
            node.Update(Visit(node.Expression));

        protected internal virtual Expression VisitMemberInit(MemberInitExpression node)
        {
            return node.Update(
                VisitAndConvert(node.NewExpression, nameof(VisitMemberInit)),
                Visit(node.Bindings, VisitMemberBinding)
            );
        }

        protected internal virtual Expression VisitMethodCall(
            MethodCallExpression node) =>
            node.Update(
                Visit(node.Object),
                Visit(node.Arguments));

        protected internal virtual Expression VisitNew(NewExpression node) =>
            node.Update(Visit(node.Arguments));

        protected internal virtual Expression VisitNewArray(NewArrayExpression node)
        {
            return node.Update(Visit(node.Expressions));
        }

        protected internal virtual Expression VisitParameter(ParameterExpression node) =>
            node;

        protected internal virtual Expression VisitTypeBinary(TypeBinaryExpression node)
        {
            return node.Update(Visit(node.Expression));
        }

        protected internal virtual Expression VisitUnary(UnaryExpression node)
        {
            Expression? operand = Visit(node.Operand);
            UnaryExpression replacement = node.Update(operand!);
            if (replacement != node && node.Method is null)
            {
                if (replacement.Method is not null)
                {
                    throw new InvalidOperationException(
                        "A visitor cannot add an operator method while rewriting a built-in unary expression.");
                }
                // rethrow has null operand
                if (node.Operand != null && replacement.Operand != null)
                {
                    ValidateChildType(node.Operand.Type, replacement.Operand.Type);
                }
            }
            return replacement;
        }

        protected internal virtual Expression VisitDebugInfo(DebugInfoExpression node)
        {
            return node;
        }

        protected internal virtual Expression VisitGoto(GotoExpression node)
        {
            return node.Update(VisitLabelTarget(node.Target), Visit(node.Value));
        }

        protected internal virtual Expression VisitLabel(LabelExpression node)
        {
            return node.Update(VisitLabelTarget(node.Target), Visit(node.DefaultValue));
        }

        [return: NotNullIfNotNull(nameof(node))]
        protected virtual LabelTarget? VisitLabelTarget(LabelTarget? node)
        {
            return node;
        }

        protected internal virtual Expression VisitLoop(LoopExpression node)
        {
            return node.Update(VisitLabelTarget(node.BreakLabel), VisitLabelTarget(node.ContinueLabel), Visit(node.Body));
        }

        protected internal virtual Expression VisitRuntimeVariables(RuntimeVariablesExpression node)
        {
            return node.Update(VisitAndConvert(node.Variables, nameof(VisitRuntimeVariables)));
        }

        protected virtual SwitchCase VisitSwitchCase(SwitchCase node)
        {
            return node.Update(Visit(node.TestValues), Visit(node.Body));
        }

        protected internal virtual Expression VisitSwitch(SwitchExpression node)
        {
            return ValidateSwitch(
                node,
                node.Update(
                    Visit(node.SwitchValue),
                    Visit(node.Cases, VisitSwitchCase),
                    Visit(node.DefaultBody)
                )
            );
        }

        protected virtual CatchBlock VisitCatchBlock(CatchBlock node)
        {
            return node.Update(VisitAndConvert(node.Variable, nameof(VisitCatchBlock)), Visit(node.Filter), Visit(node.Body));
        }

        protected internal virtual Expression VisitTry(TryExpression node)
        {
            return node.Update(
                Visit(node.Body),
                Visit(node.Handlers, VisitCatchBlock),
                Visit(node.Finally),
                Visit(node.Fault)
            );
        }

        protected virtual ElementInit VisitElementInit(ElementInit node)
        {
            return node.Update(Visit(node.Arguments));
        }

        // NetWasm: VisitMemberAssignment is deferred with the member-list and member-member binding
        // visitors, so assignments are visited here with its upstream body, and only assignment
        // bindings are constructible.
        protected virtual MemberBinding VisitMemberBinding(MemberBinding node) =>
            node.BindingType switch
            {
                MemberBindingType.Assignment => VisitMemberAssignment((MemberAssignment)node),
                MemberBindingType.MemberBinding => VisitMemberMemberBinding((MemberMemberBinding)node),
                MemberBindingType.ListBinding => VisitMemberListBinding((MemberListBinding)node),
                _ => throw Error.UnhandledBindingType(node.BindingType),
            };

        protected virtual MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {
            return node.Update(Visit(node.Expression));
        }

        protected virtual MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding node)
        {
            return node.Update(Visit(node.Bindings, VisitMemberBinding));
        }

        protected virtual MemberListBinding VisitMemberListBinding(MemberListBinding node)
        {
            return node.Update(Visit(node.Initializers, VisitElementInit));
        }

        private static SwitchExpression ValidateSwitch(SwitchExpression before, SwitchExpression after)
        {
            // If we did not have a method, we don't want to bind to one,
            // it might not be the right thing.
            if (before.Comparison == null && after.Comparison != null)
            {
                throw Error.MustRewriteWithoutMethod(after.Comparison, nameof(VisitSwitch));
            }
            return after;
        }

        private static void ValidateChildType(Type before, Type after)
        {
            if (before.IsValueType)
            {
                if (before == after)
                {
                    return;
                }
            }
            else if (!after.IsValueType)
            {
                return;
            }
            throw new InvalidOperationException(
                "A visitor must preserve value-type children when rewriting an operator.");
        }
    }
}
