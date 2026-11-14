// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.Dynamic
{
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class BinaryOperationBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected BinaryOperationBinder(System.Linq.Expressions.ExpressionType operation) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Linq.Expressions.ExpressionType Operation { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackBinaryOperation(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject arg) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackBinaryOperation(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject arg, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    public abstract partial class BindingRestrictions
    {
        internal BindingRestrictions() { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public static readonly System.Dynamic.BindingRestrictions Empty;

        public static System.Dynamic.BindingRestrictions Combine(System.Collections.Generic.IList<System.Dynamic.DynamicMetaObject>? contributingObjects) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public static System.Dynamic.BindingRestrictions GetExpressionRestriction(System.Linq.Expressions.Expression expression) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public static System.Dynamic.BindingRestrictions GetInstanceRestriction(System.Linq.Expressions.Expression expression, object? instance) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public static System.Dynamic.BindingRestrictions GetTypeRestriction(System.Linq.Expressions.Expression expression, System.Type type) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.BindingRestrictions Merge(System.Dynamic.BindingRestrictions restrictions) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Linq.Expressions.Expression ToExpression() { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class ConvertBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected ConvertBinder(System.Type type, bool @explicit) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public bool Explicit { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public System.Type Type { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[]? args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackConvert(System.Dynamic.DynamicMetaObject target) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackConvert(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class CreateInstanceBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected CreateInstanceBinder(System.Dynamic.CallInfo callInfo) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.CallInfo CallInfo { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackCreateInstance(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackCreateInstance(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class DeleteIndexBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected DeleteIndexBinder(System.Dynamic.CallInfo callInfo) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.CallInfo CallInfo { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackDeleteIndex(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] indexes) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackDeleteIndex(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] indexes, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class DeleteMemberBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected DeleteMemberBinder(string name, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public bool IgnoreCase { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public string Name { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[]? args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackDeleteMember(System.Dynamic.DynamicMetaObject target) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackDeleteMember(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    public partial class DynamicMetaObject
    {
        public static readonly System.Dynamic.DynamicMetaObject[] EmptyMetaObjects;

        public DynamicMetaObject(System.Linq.Expressions.Expression expression, System.Dynamic.BindingRestrictions restrictions) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public DynamicMetaObject(System.Linq.Expressions.Expression expression, System.Dynamic.BindingRestrictions restrictions, object value) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Linq.Expressions.Expression Expression { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public bool HasValue { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public System.Type LimitType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public System.Dynamic.BindingRestrictions Restrictions { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public System.Type? RuntimeType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public object? Value { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public virtual System.Dynamic.DynamicMetaObject BindBinaryOperation(System.Dynamic.BinaryOperationBinder binder, System.Dynamic.DynamicMetaObject arg) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindConvert(System.Dynamic.ConvertBinder binder) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindCreateInstance(System.Dynamic.CreateInstanceBinder binder, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindDeleteIndex(System.Dynamic.DeleteIndexBinder binder, System.Dynamic.DynamicMetaObject[] indexes) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindDeleteMember(System.Dynamic.DeleteMemberBinder binder) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindGetIndex(System.Dynamic.GetIndexBinder binder, System.Dynamic.DynamicMetaObject[] indexes) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindGetMember(System.Dynamic.GetMemberBinder binder) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindInvoke(System.Dynamic.InvokeBinder binder, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindInvokeMember(System.Dynamic.InvokeMemberBinder binder, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindSetIndex(System.Dynamic.SetIndexBinder binder, System.Dynamic.DynamicMetaObject[] indexes, System.Dynamic.DynamicMetaObject value) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindSetMember(System.Dynamic.SetMemberBinder binder, System.Dynamic.DynamicMetaObject value) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject BindUnaryOperation(System.Dynamic.UnaryOperationBinder binder) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public static System.Dynamic.DynamicMetaObject Create(object value, System.Linq.Expressions.Expression expression) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Collections.Generic.IEnumerable<string> GetDynamicMemberNames() { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class DynamicMetaObjectBinder : System.Runtime.CompilerServices.CallSiteBinder
    {
        protected DynamicMetaObjectBinder() { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public abstract System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args);

        public sealed override System.Linq.Expressions.Expression Bind(object[] args, System.Collections.ObjectModel.ReadOnlyCollection<System.Linq.Expressions.ParameterExpression> parameters, System.Linq.Expressions.LabelTarget returnLabel) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject Defer(System.Dynamic.DynamicMetaObject target, params System.Dynamic.DynamicMetaObject[]? args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject Defer(params System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Linq.Expressions.Expression GetUpdateExpression(System.Type type) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public partial class DynamicObject : System.Dynamic.IDynamicMetaObjectProvider
    {
        protected DynamicObject() { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Collections.Generic.IEnumerable<string> GetDynamicMemberNames() { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual System.Dynamic.DynamicMetaObject GetMetaObject(System.Linq.Expressions.Expression parameter) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryBinaryOperation(System.Dynamic.BinaryOperationBinder binder, object arg, out object? result) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryConvert(System.Dynamic.ConvertBinder binder, out object? result) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryCreateInstance(System.Dynamic.CreateInstanceBinder binder, object?[]? args, [System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out object? result) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryDeleteIndex(System.Dynamic.DeleteIndexBinder binder, object[] indexes) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryDeleteMember(System.Dynamic.DeleteMemberBinder binder) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryGetIndex(System.Dynamic.GetIndexBinder binder, object[] indexes, out object? result) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryGetMember(System.Dynamic.GetMemberBinder binder, out object? result) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryInvoke(System.Dynamic.InvokeBinder binder, object?[]? args, out object? result) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryInvokeMember(System.Dynamic.InvokeMemberBinder binder, object?[]? args, out object? result) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TrySetIndex(System.Dynamic.SetIndexBinder binder, object[] indexes, object? value) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TrySetMember(System.Dynamic.SetMemberBinder binder, object? value) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public virtual bool TryUnaryOperation(System.Dynamic.UnaryOperationBinder binder, out object? result) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class GetIndexBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected GetIndexBinder(System.Dynamic.CallInfo callInfo) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.CallInfo CallInfo { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackGetIndex(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] indexes) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackGetIndex(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] indexes, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class GetMemberBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected GetMemberBinder(string name, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public bool IgnoreCase { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public string Name { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[]? args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackGetMember(System.Dynamic.DynamicMetaObject target) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackGetMember(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    public partial interface IDynamicMetaObjectProvider
    {
        System.Dynamic.DynamicMetaObject GetMetaObject(System.Linq.Expressions.Expression parameter);
    }

    public partial interface IInvokeOnGetBinder
    {
        bool InvokeOnGet { get; }
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class InvokeBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected InvokeBinder(System.Dynamic.CallInfo callInfo) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.CallInfo CallInfo { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackInvoke(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackInvoke(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class InvokeMemberBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected InvokeMemberBinder(string name, bool ignoreCase, System.Dynamic.CallInfo callInfo) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.CallInfo CallInfo { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public bool IgnoreCase { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public string Name { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackInvoke(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args, System.Dynamic.DynamicMetaObject? errorSuggestion);

        public System.Dynamic.DynamicMetaObject FallbackInvokeMember(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackInvokeMember(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class SetIndexBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected SetIndexBinder(System.Dynamic.CallInfo callInfo) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.CallInfo CallInfo { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackSetIndex(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] indexes, System.Dynamic.DynamicMetaObject value) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackSetIndex(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] indexes, System.Dynamic.DynamicMetaObject value, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class SetMemberBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected SetMemberBinder(string name, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public bool IgnoreCase { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public string Name { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[] args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackSetMember(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject value) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackSetMember(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject value, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("Creating a call site may require dynamic code generation.")]
    public abstract partial class UnaryOperationBinder : System.Dynamic.DynamicMetaObjectBinder
    {
        protected UnaryOperationBinder(System.Linq.Expressions.ExpressionType operation) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Linq.Expressions.ExpressionType Operation { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); } }

        public sealed override System.Dynamic.DynamicMetaObject Bind(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject[]? args) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public System.Dynamic.DynamicMetaObject FallbackUnaryOperation(System.Dynamic.DynamicMetaObject target) { throw new global::System.PlatformNotSupportedException("Dynamic language binding is not available on this NetWasm platform."); }
        public abstract System.Dynamic.DynamicMetaObject FallbackUnaryOperation(System.Dynamic.DynamicMetaObject target, System.Dynamic.DynamicMetaObject? errorSuggestion);
    }
}
