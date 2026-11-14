// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text;

namespace System.Linq.Expressions
{
    internal sealed class ExpressionStringBuilder : ExpressionVisitor
    {
        private readonly StringBuilder _builder = new();
        // Associate every unique label or anonymous parameter in the tree with an integer.
        // Labels are displayed as UnnamedLabel_#; parameters are displayed as Param_#.
        private Dictionary<object, int>? _ids;

        private ExpressionStringBuilder()
        {
        }

        internal static string ExpressionToString(Expression node)
        {
            var builder = new ExpressionStringBuilder();
            builder.Visit(node);
            return builder._builder.ToString();
        }

        internal static string CatchBlockToString(CatchBlock node)
        {
            var builder = new ExpressionStringBuilder();
            builder.VisitCatchBlock(node);
            return builder._builder.ToString();
        }

        internal static string SwitchCaseToString(SwitchCase node)
        {
            var builder = new ExpressionStringBuilder();
            builder.VisitSwitchCase(node);
            return builder._builder.ToString();
        }

        internal static string MemberBindingToString(MemberBinding node)
        {
            var builder = new ExpressionStringBuilder();
            builder.VisitMemberBinding(node);
            return builder._builder.ToString();
        }

        internal static string ElementInitBindingToString(ElementInit node)
        {
            var builder = new ExpressionStringBuilder();
            builder.VisitElementInit(node);
            return builder._builder.ToString();
        }

        protected internal override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.ArrayIndex)
            {
                Visit(node.Left);
                _builder.Append('[');
                Visit(node.Right);
                _builder.Append(']');
                return node;
            }
            _builder.Append('(');
            Visit(node.Left);
            _builder.Append(' ').Append(BinaryOperator(node)).Append(' ');
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

        protected internal override Expression VisitIndex(IndexExpression node)
        {
            if (node.Object != null)
            {
                Visit(node.Object);
            }
            else
            {
                _builder.Append(node.Indexer!.DeclaringType!.Name);
            }
            if (node.Indexer != null)
            {
                _builder.Append('.').Append(node.Indexer.Name);
            }
            _builder.Append('[');
            AppendArguments(node.Arguments, leadingComma: false);
            _builder.Append(']');
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

        protected internal override Expression VisitListInit(ListInitExpression node)
        {
            Visit(node.NewExpression);
            _builder.Append(" {");
            for (var index = 0; index < node.Initializers.Count; index++)
            {
                if (index != 0)
                {
                    _builder.Append(", ");
                }
                VisitElementInit(node.Initializers[index]);
            }
            _builder.Append('}');
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

        protected internal override Expression VisitMemberInit(MemberInitExpression node)
        {
            if (node.NewExpression.Arguments.Count == 0 &&
                node.NewExpression.Type.Name.Contains('<'))
            {
                // anonymous type constructor
                _builder.Append("new");
            }
            else
            {
                Visit(node.NewExpression);
            }
            _builder.Append(" {");
            for (var index = 0; index < node.Bindings.Count; index++)
            {
                if (index != 0)
                {
                    _builder.Append(", ");
                }
                VisitMemberBinding(node.Bindings[index]);
            }
            _builder.Append('}');
            return node;
        }

        protected override MemberAssignment VisitMemberAssignment(MemberAssignment assignment)
        {
            _builder.Append(assignment.Member.Name).Append(" = ");
            Visit(assignment.Expression);
            return assignment;
        }

        protected override MemberListBinding VisitMemberListBinding(MemberListBinding binding)
        {
            _builder.Append(binding.Member.Name).Append(" = {");
            for (int i = 0, n = binding.Initializers.Count; i < n; i++)
            {
                if (i > 0)
                {
                    _builder.Append(", ");
                }
                VisitElementInit(binding.Initializers[i]);
            }
            _builder.Append('}');
            return binding;
        }

        protected override MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding binding)
        {
            _builder.Append(binding.Member.Name).Append(" = {");
            for (int i = 0, n = binding.Bindings.Count; i < n; i++)
            {
                if (i > 0)
                {
                    _builder.Append(", ");
                }
                VisitMemberBinding(binding.Bindings[i]);
            }
            _builder.Append('}');
            return binding;
        }

        protected override ElementInit VisitElementInit(ElementInit initializer)
        {
            _builder.Append(initializer.AddMethod.ToString()).Append('(');
            AppendArguments(initializer.Arguments, leadingComma: false);
            _builder.Append(')');
            return initializer;
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
            _builder.Append("new ").Append(node.Type.Name).Append('(');
            System.Collections.ObjectModel.ReadOnlyCollection<System.Reflection.MemberInfo>? members = node.Members;
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    _builder.Append(", ");
                }
                if (members != null)
                {
                    _builder.Append(members[i].Name).Append(" = ");
                }
                Visit(node.Arguments[i]);
            }
            _builder.Append(')');
            return node;
        }

        protected internal override Expression VisitNewArray(NewArrayExpression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.NewArrayBounds:
                    // new MyType[](expr1, expr2)
                    _builder.Append("new ").Append(node.Type.ToString()).Append('(');
                    AppendArguments(node.Expressions, leadingComma: false);
                    _builder.Append(')');
                    break;
                case ExpressionType.NewArrayInit:
                    // new [] {expr1, expr2}
                    _builder.Append("new [] {");
                    AppendArguments(node.Expressions, leadingComma: false);
                    _builder.Append('}');
                    break;
            }
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
                _builder.Append("Param_").Append(GetId(node));
            }
            return node;
        }

        protected internal override Expression VisitTypeBinary(TypeBinaryExpression node)
        {
            _builder.Append('(');
            Visit(node.Expression);
            switch (node.NodeType)
            {
                case ExpressionType.TypeIs:
                    _builder.Append(" Is ");
                    break;
                case ExpressionType.TypeEqual:
                    _builder.Append(" TypeEqual ");
                    break;
            }
            _builder.Append(node.TypeOperand.Name).Append(')');
            return node;
        }

        protected internal override Expression VisitUnary(UnaryExpression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked: _builder.Append('-'); break;
                case ExpressionType.Not: _builder.Append("Not("); break;
                case ExpressionType.IsFalse: _builder.Append("IsFalse("); break;
                case ExpressionType.IsTrue: _builder.Append("IsTrue("); break;
                case ExpressionType.OnesComplement: _builder.Append("~("); break;
                case ExpressionType.ArrayLength: _builder.Append("ArrayLength("); break;
                case ExpressionType.Convert: _builder.Append("Convert("); break;
                case ExpressionType.ConvertChecked: _builder.Append("ConvertChecked("); break;
                case ExpressionType.Throw: _builder.Append("throw("); break;
                case ExpressionType.TypeAs: _builder.Append('('); break;
                case ExpressionType.UnaryPlus: _builder.Append('+'); break;
                case ExpressionType.Unbox: _builder.Append("Unbox("); break;
                case ExpressionType.Increment: _builder.Append("Increment("); break;
                case ExpressionType.Decrement: _builder.Append("Decrement("); break;
                case ExpressionType.PreIncrementAssign: _builder.Append("++"); break;
                case ExpressionType.PreDecrementAssign: _builder.Append("--"); break;
                case ExpressionType.Quote:
                case ExpressionType.PostIncrementAssign:
                case ExpressionType.PostDecrementAssign:
                    break;
                default:
                    throw new InvalidOperationException();
            }

            Visit(node.Operand);

            switch (node.NodeType)
            {
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.UnaryPlus:
                case ExpressionType.PreDecrementAssign:
                case ExpressionType.PreIncrementAssign:
                case ExpressionType.Quote:
                    break;
                case ExpressionType.TypeAs:
                    _builder.Append(" As ").Append(node.Type.Name).Append(')');
                    break;
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    _builder.Append(", ").Append(node.Type.Name).Append(')');
                    break;
                case ExpressionType.PostIncrementAssign: _builder.Append("++"); break;
                case ExpressionType.PostDecrementAssign: _builder.Append("--"); break;
                default:
                    _builder.Append(')');
                    break;
            }
            return node;
        }

        protected internal override Expression VisitDebugInfo(DebugInfoExpression node)
        {
            // NetWasm: upstream interpolates; the line and column numbers are appended directly.
            _builder.Append("<DebugInfo(").Append(node.Document.FileName).Append(": ")
                .Append(node.StartLine).Append(", ").Append(node.StartColumn).Append(", ")
                .Append(node.EndLine).Append(", ").Append(node.EndColumn).Append(")>");
            return node;
        }

        protected internal override Expression VisitRuntimeVariables(RuntimeVariablesExpression node)
        {
            _builder.Append('(');
            AppendArguments(node.Variables, leadingComma: false);
            _builder.Append(')');
            return node;
        }

        protected internal override Expression VisitLabel(LabelExpression node)
        {
            _builder.Append("{ ... } ");
            DumpLabel(node.Target);
            _builder.Append(':');
            return node;
        }

        protected internal override Expression VisitGoto(GotoExpression node)
        {
            string op = node.Kind switch
            {
                GotoExpressionKind.Goto => "goto",
                GotoExpressionKind.Break => "break",
                GotoExpressionKind.Continue => "continue",
                GotoExpressionKind.Return => "return",
                _ => throw new InvalidOperationException(),
            };
            _builder.Append(op).Append(' ');
            DumpLabel(node.Target);
            if (node.Value != null)
            {
                _builder.Append(" (");
                Visit(node.Value);
                _builder.Append(')');
            }
            return node;
        }

        protected internal override Expression VisitLoop(LoopExpression node)
        {
            _builder.Append("loop { ... }");
            return node;
        }

        protected override SwitchCase VisitSwitchCase(SwitchCase node)
        {
            _builder.Append("case (");
            AppendArguments(node.TestValues, leadingComma: false);
            _builder.Append("): ...");
            return node;
        }

        protected internal override Expression VisitSwitch(SwitchExpression node)
        {
            _builder.Append("switch (");
            Visit(node.SwitchValue);
            _builder.Append(") { ... }");
            return node;
        }

        protected override CatchBlock VisitCatchBlock(CatchBlock node)
        {
            _builder.Append("catch (").Append(node.Test.Name);
            if (!string.IsNullOrEmpty(node.Variable?.Name))
            {
                _builder.Append(' ').Append(node.Variable.Name);
            }
            _builder.Append(") { ... }");
            return node;
        }

        protected internal override Expression VisitTry(TryExpression node)
        {
            _builder.Append("try { ... }");
            return node;
        }

        private void DumpLabel(LabelTarget target)
        {
            if (!string.IsNullOrEmpty(target.Name))
            {
                _builder.Append(target.Name);
            }
            else
            {
                _builder.Append("UnnamedLabel_").Append(GetId(target));
            }
        }

        private int GetId(object o)
        {
            _ids ??= new Dictionary<object, int>();

            int id;
            if (!_ids.TryGetValue(o, out id))
            {
                id = _ids.Count;
                _ids.Add(o, id);
            }

            return id;
        }

        private void AppendArguments<T>(
            System.Collections.ObjectModel.ReadOnlyCollection<T> arguments,
            bool leadingComma)
            where T : Expression
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

        // Upstream's operator spellings.
        private static string BinaryOperator(BinaryExpression node) =>
            node.NodeType switch
            {
                ExpressionType.AndAlso => "AndAlso",
                ExpressionType.OrElse => "OrElse",
                ExpressionType.Assign => "=",
                ExpressionType.Equal => "==",
                ExpressionType.NotEqual => "!=",
                ExpressionType.GreaterThan => ">",
                ExpressionType.LessThan => "<",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThanOrEqual => "<=",
                ExpressionType.Add or ExpressionType.AddChecked => "+",
                ExpressionType.AddAssign or ExpressionType.AddAssignChecked => "+=",
                ExpressionType.Subtract or ExpressionType.SubtractChecked => "-",
                ExpressionType.SubtractAssign or ExpressionType.SubtractAssignChecked => "-=",
                ExpressionType.Divide => "/",
                ExpressionType.DivideAssign => "/=",
                ExpressionType.Modulo => "%",
                ExpressionType.ModuloAssign => "%=",
                ExpressionType.Multiply or ExpressionType.MultiplyChecked => "*",
                ExpressionType.MultiplyAssign or ExpressionType.MultiplyAssignChecked => "*=",
                ExpressionType.LeftShift => "<<",
                ExpressionType.LeftShiftAssign => "<<=",
                ExpressionType.RightShift => ">>",
                ExpressionType.RightShiftAssign => ">>=",
                ExpressionType.And => IsBool(node) ? "And" : "&",
                ExpressionType.AndAssign => IsBool(node) ? "&&=" : "&=",
                ExpressionType.Or => IsBool(node) ? "Or" : "|",
                ExpressionType.OrAssign => IsBool(node) ? "||=" : "|=",
                ExpressionType.ExclusiveOr => "^",
                ExpressionType.ExclusiveOrAssign => "^=",
                ExpressionType.Power => "**",
                ExpressionType.PowerAssign => "**=",
                ExpressionType.Coalesce => "??",
                _ => throw new InvalidOperationException(),
            };

        private static bool IsBool(Expression node) =>
            node.Type == typeof(bool) || node.Type == typeof(bool?);
    }
}
