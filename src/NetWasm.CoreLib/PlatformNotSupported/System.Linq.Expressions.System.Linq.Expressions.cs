// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.Linq.Expressions
{
    public partial class DynamicExpression : System.Linq.Expressions.Expression, System.Linq.Expressions.IArgumentProvider, System.Linq.Expressions.IDynamicExpression
    {
        internal DynamicExpression() { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        public System.Collections.ObjectModel.ReadOnlyCollection<System.Linq.Expressions.Expression> Arguments { get { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); } }

        public System.Runtime.CompilerServices.CallSiteBinder Binder { get { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); } }

        public System.Type DelegateType { get { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); } }

        public sealed override System.Linq.Expressions.ExpressionType NodeType { get { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); } }

        int System.Linq.Expressions.IArgumentProvider.ArgumentCount { get { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); } }

        public override System.Type Type { get { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); } }

        protected internal override System.Linq.Expressions.Expression Accept(System.Linq.Expressions.ExpressionVisitor visitor) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Delegate creation requires dynamic code generation.")]
        public static new System.Linq.Expressions.DynamicExpression Dynamic(System.Runtime.CompilerServices.CallSiteBinder binder, System.Type returnType, System.Collections.Generic.IEnumerable<System.Linq.Expressions.Expression> arguments) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Delegate creation requires dynamic code generation.")]
        public static new System.Linq.Expressions.DynamicExpression Dynamic(System.Runtime.CompilerServices.CallSiteBinder binder, System.Type returnType, System.Linq.Expressions.Expression arg0) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Delegate creation requires dynamic code generation.")]
        public static new System.Linq.Expressions.DynamicExpression Dynamic(System.Runtime.CompilerServices.CallSiteBinder binder, System.Type returnType, System.Linq.Expressions.Expression arg0, System.Linq.Expressions.Expression arg1) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Delegate creation requires dynamic code generation.")]
        public static new System.Linq.Expressions.DynamicExpression Dynamic(System.Runtime.CompilerServices.CallSiteBinder binder, System.Type returnType, System.Linq.Expressions.Expression arg0, System.Linq.Expressions.Expression arg1, System.Linq.Expressions.Expression arg2) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Delegate creation requires dynamic code generation.")]
        public static new System.Linq.Expressions.DynamicExpression Dynamic(System.Runtime.CompilerServices.CallSiteBinder binder, System.Type returnType, System.Linq.Expressions.Expression arg0, System.Linq.Expressions.Expression arg1, System.Linq.Expressions.Expression arg2, System.Linq.Expressions.Expression arg3) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Delegate creation requires dynamic code generation.")]
        public static new System.Linq.Expressions.DynamicExpression Dynamic(System.Runtime.CompilerServices.CallSiteBinder binder, System.Type returnType, params System.Linq.Expressions.Expression[] arguments) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Delegate creation requires dynamic code generation.")]
        public static new System.Linq.Expressions.DynamicExpression MakeDynamic(System.Type delegateType, System.Runtime.CompilerServices.CallSiteBinder binder, System.Collections.Generic.IEnumerable<System.Linq.Expressions.Expression>? arguments) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        public static new System.Linq.Expressions.DynamicExpression MakeDynamic(System.Type delegateType, System.Runtime.CompilerServices.CallSiteBinder binder, System.Linq.Expressions.Expression arg0) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        public static new System.Linq.Expressions.DynamicExpression MakeDynamic(System.Type delegateType, System.Runtime.CompilerServices.CallSiteBinder binder, System.Linq.Expressions.Expression arg0, System.Linq.Expressions.Expression arg1) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        public static new System.Linq.Expressions.DynamicExpression MakeDynamic(System.Type delegateType, System.Runtime.CompilerServices.CallSiteBinder binder, System.Linq.Expressions.Expression arg0, System.Linq.Expressions.Expression arg1, System.Linq.Expressions.Expression arg2) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        public static new System.Linq.Expressions.DynamicExpression MakeDynamic(System.Type delegateType, System.Runtime.CompilerServices.CallSiteBinder binder, System.Linq.Expressions.Expression arg0, System.Linq.Expressions.Expression arg1, System.Linq.Expressions.Expression arg2, System.Linq.Expressions.Expression arg3) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        public static new System.Linq.Expressions.DynamicExpression MakeDynamic(System.Type delegateType, System.Runtime.CompilerServices.CallSiteBinder binder, params System.Linq.Expressions.Expression[]? arguments) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        System.Linq.Expressions.Expression System.Linq.Expressions.IArgumentProvider.GetArgument(int index) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        object System.Linq.Expressions.IDynamicExpression.CreateCallSite() { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        System.Linq.Expressions.Expression System.Linq.Expressions.IDynamicExpression.Rewrite(System.Linq.Expressions.Expression[] args) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        public System.Linq.Expressions.DynamicExpression Update(System.Collections.Generic.IEnumerable<System.Linq.Expressions.Expression>? arguments) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
    }

    public partial class DynamicExpressionVisitor : System.Linq.Expressions.ExpressionVisitor
    {
        public DynamicExpressionVisitor() { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
        protected internal override System.Linq.Expressions.Expression VisitDynamic(System.Linq.Expressions.DynamicExpression node) { throw new global::System.PlatformNotSupportedException("Dynamic expression binding is not available on this NetWasm platform."); }
    }

    public partial interface IDynamicExpression : System.Linq.Expressions.IArgumentProvider
    {
        System.Type DelegateType { get; }

        object CreateCallSite();

        System.Linq.Expressions.Expression Rewrite(System.Linq.Expressions.Expression[] args);
    }
}
