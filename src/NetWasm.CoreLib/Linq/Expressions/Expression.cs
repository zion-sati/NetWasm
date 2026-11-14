// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Linq.Expressions
{
    public abstract partial class Expression
    {
        // Upstream's trimming messages, for the RequiresUnreferencedCode and RequiresDynamicCode
        // annotations the ported factories keep.
        internal const string ExpressionRequiresUnreferencedCode = "Creating Expressions requires unreferenced code because the members being referenced by the Expression may be trimmed.";
        internal const string PropertyFromAccessorRequiresUnreferencedCode = "The Property metadata or other accessor may be trimmed.";
        internal const string GenericMethodRequiresDynamicCode = "Calling a generic method requires dynamic code generation. This can be suppressed if the method is not generic.";
        internal const string NewArrayRequiresDynamicCode = "Creating arrays at runtime requires dynamic code generation.";
        internal const string DelegateCreationRequiresDynamicCode = "Delegate creation requires dynamic code generation.";

        private sealed class ExtensionInfo
        {
            public ExtensionInfo(ExpressionType nodeType, Type type)
            {
                NodeType = nodeType;
                Type = type;
            }

            internal readonly ExpressionType NodeType;
            internal readonly Type Type;
        }

        private static ConditionalWeakTable<Expression, ExtensionInfo>? s_legacyCtorSupportTable;

        [Obsolete("This constructor has been deprecated. Use a different constructor that does not take ExpressionType. Then override NodeType and Type properties to provide the values that would be specified to this constructor.")]
        protected Expression(ExpressionType nodeType, Type type)
        {
            // Can't enforce anything that V1 didn't
            if (s_legacyCtorSupportTable == null)
            {
                Interlocked.CompareExchange(
                    ref s_legacyCtorSupportTable,
                    new ConditionalWeakTable<Expression, ExtensionInfo>(),
                    comparand: null
                );
            }

            s_legacyCtorSupportTable.Add(this, new ExtensionInfo(nodeType, type));
        }

        protected Expression()
        {
        }

        public virtual ExpressionType NodeType
        {
            get
            {
                if (s_legacyCtorSupportTable != null && s_legacyCtorSupportTable.TryGetValue(this, out ExtensionInfo? extInfo))
                {
                    return extInfo.NodeType;
                }

                // the extension expression failed to override NodeType
                throw Error.ExtensionNodeMustOverrideProperty("Expression.NodeType");
            }
        }

        public virtual Type Type
        {
            get
            {
                if (s_legacyCtorSupportTable != null && s_legacyCtorSupportTable.TryGetValue(this, out ExtensionInfo? extInfo))
                {
                    return extInfo.Type;
                }

                // the extension expression failed to override Type
                throw Error.ExtensionNodeMustOverrideProperty("Expression.Type");
            }
        }

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

        private static void RequiresCanRead(IReadOnlyList<Expression> items, string paramName)
        {
            Debug.Assert(items != null);
            // this is called a lot, avoid allocating an enumerator if we can...
            for (int i = 0, n = items.Count; i < n; i++)
            {
                ExpressionUtils.RequiresCanRead(items[i], paramName, i);
            }
        }

        private static void RequiresCanWrite(Expression expression, string paramName)
        {
            ArgumentNullException.ThrowIfNull(expression, paramName);

            switch (expression.NodeType)
            {
                case ExpressionType.Index:
                    PropertyInfo? indexer = ((IndexExpression)expression).Indexer;
                    if (indexer == null || indexer.CanWrite)
                    {
                        return;
                    }
                    break;
                case ExpressionType.MemberAccess:
                    MemberInfo member = ((MemberExpression)expression).Member;
                    if (member is PropertyInfo prop)
                    {
                        if (prop.CanWrite)
                        {
                            return;
                        }
                    }
                    else
                    {
                        Debug.Assert(member is FieldInfo);
                        FieldInfo field = (FieldInfo)member;
                        if (!(field.IsInitOnly || field.IsLiteral))
                        {
                            return;
                        }
                    }
                    break;
                case ExpressionType.Parameter:
                    return;
            }

            throw Error.ExpressionMustBeWriteable(paramName);
        }
    }
}
