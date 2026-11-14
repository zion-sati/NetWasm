// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: the Lambda(Type, ...) overloads validate as upstream (ValidateLambdaArgs, with
// Invoke from the compiler's delegate fact and the by-ref parameter type reported unwrapped), then
// throw PNSE, because creating Expression<TDelegate> for a run-time delegate Type needs runtime
// generic instantiation. The overloads that infer the delegate type, and GetFuncType, GetActionType
// and GetDelegateType, are upstream's; constructing a Func or Action instantiation throws PNSE from
// Type.MakeGenericType, so only the parameterless Action resolves. Compile(DebugInfoGenerator) is
// upstream's interpreter overload, which ignores the generator.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic.Utils;
using System.Reflection;
using System.Linq.Expressions.Interpreter;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions
{
    public abstract class LambdaExpression : Expression
    {
        private readonly Expression _body;
        private readonly ReadOnlyCollection<ParameterExpression> _parameters;

        internal LambdaExpression(
            Expression body,
            ParameterExpression[] parameters,
            string? name,
            bool tailCall)
        {
            _body = body;
            _parameters = parameters.Length == 0
                ? ReadOnlyCollection<ParameterExpression>.Empty
                : new ReadOnlyCollection<ParameterExpression>(parameters);
            Name = name;
            TailCall = tailCall;
        }

        public static bool CanCompileToIL => false;

        public static bool CanInterpret => true;

        public sealed override ExpressionType NodeType => ExpressionType.Lambda;

        public sealed override Type Type => TypeCore;

        internal abstract Type TypeCore { get; }

        internal abstract Type PublicType { get; }

        public Expression Body => _body;

        public ReadOnlyCollection<ParameterExpression> Parameters => _parameters;

        public string? Name { get; }

        public bool TailCall { get; }

        public Type ReturnType => InvokeMethod.ReturnType;

        // The delegate type's Invoke, which the lambda's type names when compiling.
        internal abstract MethodInfo InvokeMethod { get; }

        public Delegate Compile() => Compile(preferInterpretation: false);

        public Delegate Compile(bool preferInterpretation) => CompileCore();

        public Delegate Compile(DebugInfoGenerator debugInfoGenerator)
        {
            return Compile();
        }

        internal abstract Delegate CompileCore();

        // A nested lambda's delegate over the interpreter's target, of the delegate type the lambda names: its
        // Expression<TDelegate> holds the adapter the compiler plans for TDelegate.
        internal abstract Delegate CreateInterpretedDelegate(
            System.Runtime.CompilerServices.ObjectArrayDelegateTarget target);

        internal bool HasSameParameters(IEnumerable<ParameterExpression>? parameters)
        {
            if (parameters is null)
            {
                return _parameters.Count == 0;
            }
            using IEnumerator<ParameterExpression> enumerator = parameters.GetEnumerator();
            for (var index = 0; index < _parameters.Count; index++)
            {
                if (!enumerator.MoveNext() ||
                    !ReferenceEquals(enumerator.Current, _parameters[index]))
                {
                    return false;
                }
            }
            return !enumerator.MoveNext();
        }
    }

    public partial class Expression<TDelegate> : LambdaExpression
    {
        internal Expression(
            Expression body,
            ParameterExpression[] parameters,
            string? name,
            bool tailCall)
            : base(body, parameters, name, tailCall)
        {
        }

        internal sealed override Type TypeCore => typeof(TDelegate);

        internal sealed override MethodInfo InvokeMethod =>
            System.Runtime.CompilerServices.DelegateMetadata.GetInvokeMethod<TDelegate>();

        public Expression<TDelegate> Update(
            Expression body,
            IEnumerable<ParameterExpression>? parameters)
        {
            ArgumentNullException.ThrowIfNull(body);
            ParameterExpression[] parameterArray =
                Expression.CopyParameters(parameters);
            if (ReferenceEquals(body, Body) &&
                HasSameParameters(parameterArray))
            {
                return this;
            }
            return Lambda<TDelegate>(body, Name, TailCall, parameterArray);
        }

        internal sealed override Type PublicType => typeof(Expression<TDelegate>);

        public new TDelegate Compile() => Compile(preferInterpretation: false);

        public new TDelegate Compile(bool preferInterpretation) =>
            ObjectArrayDelegateAdapter.Create<TDelegate>(LightCompiler.Compile(this));

        public new TDelegate Compile(DebugInfoGenerator debugInfoGenerator)
        {
            return Compile();
        }

        internal sealed override Delegate CompileCore() =>
            (Delegate)(object)Compile()!;

        internal sealed override Delegate CreateInterpretedDelegate(
            System.Runtime.CompilerServices.ObjectArrayDelegateTarget target) =>
            (Delegate)(object)ObjectArrayDelegateAdapter.Create<TDelegate>(target)!;

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitLambda(this);
    }

    public abstract partial class Expression
    {
        public static Expression<TDelegate> Lambda<TDelegate>(
            Expression body,
            params ParameterExpression[]? parameters) =>
            Lambda<TDelegate>(body, name: null, tailCall: false, parameters);

        public static Expression<TDelegate> Lambda<TDelegate>(
            Expression body,
            bool tailCall,
            params ParameterExpression[]? parameters) =>
            Lambda<TDelegate>(body, name: null, tailCall, parameters);

        public static Expression<TDelegate> Lambda<TDelegate>(
            Expression body,
            IEnumerable<ParameterExpression>? parameters) =>
            Lambda<TDelegate>(body, name: null, tailCall: false, parameters);

        public static Expression<TDelegate> Lambda<TDelegate>(
            Expression body,
            bool tailCall,
            IEnumerable<ParameterExpression>? parameters) =>
            Lambda<TDelegate>(body, name: null, tailCall, parameters);

        public static Expression<TDelegate> Lambda<TDelegate>(
            Expression body,
            string? name,
            IEnumerable<ParameterExpression>? parameters) =>
            Lambda<TDelegate>(body, name, tailCall: false, parameters);

        public static Expression<TDelegate> Lambda<TDelegate>(
            Expression body,
            string? name,
            bool tailCall,
            IEnumerable<ParameterExpression>? parameters)
        {
            ArgumentNullException.ThrowIfNull(body);
            ParameterExpression[] parameterArray = CopyParameters(parameters);
            ValidateLambda<TDelegate>(ref body, parameterArray);
            return new Expression<TDelegate>(body, parameterArray, name, tailCall);
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static LambdaExpression Lambda(Expression body, params ParameterExpression[]? parameters)
        {
            return Lambda(body, false, (IEnumerable<ParameterExpression>?)parameters);
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static LambdaExpression Lambda(Expression body, bool tailCall, params ParameterExpression[]? parameters)
        {
            return Lambda(body, tailCall, (IEnumerable<ParameterExpression>?)parameters);
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static LambdaExpression Lambda(Expression body, IEnumerable<ParameterExpression>? parameters)
        {
            return Lambda(body, null, false, parameters);
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static LambdaExpression Lambda(Expression body, bool tailCall, IEnumerable<ParameterExpression>? parameters)
        {
            return Lambda(body, null, tailCall, parameters);
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static LambdaExpression Lambda(Expression body, string? name, IEnumerable<ParameterExpression>? parameters)
        {
            return Lambda(body, name, false, parameters);
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static LambdaExpression Lambda(Expression body, string? name, bool tailCall, IEnumerable<ParameterExpression>? parameters)
        {
            ArgumentNullException.ThrowIfNull(body);

            ReadOnlyCollection<ParameterExpression> parameterList = parameters.ToReadOnly();

            int paramCount = parameterList.Count;
            Type[] typeArgs = new Type[paramCount + 1];
            if (paramCount > 0)
            {
                var set = new HashSet<ParameterExpression>();
                for (int i = 0; i < paramCount; i++)
                {
                    ParameterExpression param = parameterList[i];
                    ArgumentNullException.ThrowIfNull(param, "parameter");
                    typeArgs[i] = param.IsByRef ? param.Type.MakeByRefType() : param.Type;
                    if (!set.Add(param))
                    {
                        throw Error.DuplicateVariable(param, nameof(parameters), i);
                    }
                }
            }
            typeArgs[paramCount] = body.Type;

            Type delegateType = Compiler.DelegateHelpers.MakeDelegateType(typeArgs);

            return CreateLambda(delegateType, body, name, tailCall, parameterList);
        }

        public static LambdaExpression Lambda(Type delegateType, Expression body, params ParameterExpression[]? parameters)
        {
            return Lambda(delegateType, body, null, false, parameters);
        }

        public static LambdaExpression Lambda(Type delegateType, Expression body, bool tailCall, params ParameterExpression[]? parameters)
        {
            return Lambda(delegateType, body, null, tailCall, parameters);
        }

        public static LambdaExpression Lambda(Type delegateType, Expression body, IEnumerable<ParameterExpression>? parameters)
        {
            return Lambda(delegateType, body, null, false, parameters);
        }

        public static LambdaExpression Lambda(Type delegateType, Expression body, bool tailCall, IEnumerable<ParameterExpression>? parameters)
        {
            return Lambda(delegateType, body, null, tailCall, parameters);
        }

        public static LambdaExpression Lambda(Type delegateType, Expression body, string? name, IEnumerable<ParameterExpression>? parameters)
        {
            ReadOnlyCollection<ParameterExpression> paramList = parameters.ToReadOnly();
            ValidateLambdaArgs(delegateType, ref body, paramList, nameof(delegateType));

            return CreateLambda(delegateType, body, name, false, paramList);
        }

        public static LambdaExpression Lambda(Type delegateType, Expression body, string? name, bool tailCall, IEnumerable<ParameterExpression>? parameters)
        {
            ReadOnlyCollection<ParameterExpression> paramList = parameters.ToReadOnly();
            ValidateLambdaArgs(delegateType, ref body, paramList, nameof(delegateType));

            return CreateLambda(delegateType, body, name, tailCall, paramList);
        }

        private static MethodInfo RuntimeDelegateInvoke(Type delegateType) =>
            throw System.Runtime.CompilerServices.DelegateMetadata.UnknownDelegateType();

        private static void ValidateLambdaArgs(Type delegateType, ref Expression body, ReadOnlyCollection<ParameterExpression> parameters, string paramName)
        {
            ArgumentNullException.ThrowIfNull(delegateType);
            ExpressionUtils.RequiresCanRead(body, nameof(body));

            if (!typeof(MulticastDelegate).IsAssignableFrom(delegateType) || delegateType == typeof(MulticastDelegate))
            {
                throw Error.LambdaTypeMustBeDerivedFromSystemDelegate(paramName);
            }

            TypeUtils.ValidateType(delegateType, nameof(delegateType), allowByRef: true, allowPointer: true);

            // NetWasm: a delegate type known only at run time has no Invoke metadata, so a lambda of it cannot be
            // built (CreateLambda refuses it too); the arguments are still checked as upstream checks them first.
            MethodInfo mi = RuntimeDelegateInvoke(delegateType);

            ParameterInfo[] pis = mi.GetParameters();

            if (pis.Length > 0)
            {
                if (pis.Length != parameters.Count)
                {
                    throw Error.IncorrectNumberOfLambdaDeclarationParameters();
                }
                var set = new HashSet<ParameterExpression>();
                for (int i = 0, n = pis.Length; i < n; i++)
                {
                    ParameterExpression pex = parameters[i];
                    ParameterInfo pi = pis[i];
                    ExpressionUtils.RequiresCanRead(pex, nameof(parameters), i);
                    Type pType = pi.ParameterType;
                    if (pex.IsByRef)
                    {
                        if (!pType.IsByRef)
                        {
                            //We cannot pass a parameter of T& to a delegate that takes T or any non-ByRef type.
                            throw Error.ParameterExpressionNotValidAsDelegate(pex.Type, pType);
                        }
                        pType = pType.GetElementType()!;
                    }
                    if (!TypeUtils.AreReferenceAssignable(pex.Type, pType))
                    {
                        throw Error.ParameterExpressionNotValidAsDelegate(pex.Type, pType);
                    }
                    if (!set.Add(pex))
                    {
                        throw Error.DuplicateVariable(pex, nameof(parameters), i);
                    }
                }
            }
            else if (parameters.Count > 0)
            {
                throw Error.IncorrectNumberOfLambdaDeclarationParameters();
            }
            if (mi.ReturnType != typeof(void) && !TypeUtils.AreReferenceAssignable(mi.ReturnType, body.Type))
            {
                if (!ExpressionUtils.TryQuote(mi.ReturnType, ref body))
                {
                    throw Error.ExpressionTypeDoesNotMatchReturn(body.Type, mi.ReturnType);
                }
            }
        }

        // NetWasm: upstream instantiates Expression<TDelegate> for the supplied delegate type at run time
        // (MakeGenericType). NetWasm cannot construct a generic type at run time, so after validation the
        // non-generic Lambda overloads throw PNSE; Expression.Lambda<TDelegate> remains available.
        private static LambdaExpression CreateLambda(Type delegateType, Expression body, string? name, bool tailCall, ReadOnlyCollection<ParameterExpression> parameters)
        {
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_RuntimeLambdaType);
        }

        private enum TryGetFuncActionArgsResult
        {
            Valid,
            ArgumentNull,
            ByRef,
            PointerOrVoid
        }

        private static TryGetFuncActionArgsResult ValidateTryGetFuncActionArgs(Type[]? typeArgs)
        {
            if (typeArgs == null)
            {
                return TryGetFuncActionArgsResult.ArgumentNull;
            }

            for (int i = 0; i < typeArgs.Length; i++)
            {
                Type a = typeArgs[i];
                if (a == null)
                {
                    return TryGetFuncActionArgsResult.ArgumentNull;
                }

                if (a.IsByRef)
                {
                    return TryGetFuncActionArgsResult.ByRef;
                }

                if (a == typeof(void) || a.IsPointer)
                {
                    return TryGetFuncActionArgsResult.PointerOrVoid;
                }
            }

            return TryGetFuncActionArgsResult.Valid;
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static Type GetFuncType(params Type[]? typeArgs)
        {
            switch (ValidateTryGetFuncActionArgs(typeArgs))
            {
                case TryGetFuncActionArgsResult.ArgumentNull:
                    throw new ArgumentNullException(nameof(typeArgs));
                case TryGetFuncActionArgsResult.ByRef:
                    throw Error.TypeMustNotBeByRef(nameof(typeArgs));
                default:

                    // This includes pointers or void. We allow the exception that comes
                    // from trying to use them as generic arguments to pass through.
                    // NetWasm: ValidateTryGetFuncActionArgs has rejected a null array.
                    Type? result = Compiler.DelegateHelpers.GetFuncType(typeArgs!);
                    if (result == null)
                    {
                        throw Error.IncorrectNumberOfTypeArgsForFunc(nameof(typeArgs));
                    }

                    return result;
            }
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static bool TryGetFuncType(Type[] typeArgs, [NotNullWhen(true)] out Type? funcType)
        {
            if (ValidateTryGetFuncActionArgs(typeArgs) == TryGetFuncActionArgsResult.Valid)
            {
                return (funcType = Compiler.DelegateHelpers.GetFuncType(typeArgs)) != null;
            }

            funcType = null;
            return false;
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static Type GetActionType(params Type[]? typeArgs)
        {
            switch (ValidateTryGetFuncActionArgs(typeArgs))
            {
                case TryGetFuncActionArgsResult.ArgumentNull:
                    throw new ArgumentNullException(nameof(typeArgs));
                case TryGetFuncActionArgsResult.ByRef:
                    throw Error.TypeMustNotBeByRef(nameof(typeArgs));
                default:

                    // This includes pointers or void. We allow the exception that comes
                    // from trying to use them as generic arguments to pass through.
                    // NetWasm: ValidateTryGetFuncActionArgs has rejected a null array.
                    Type? result = Compiler.DelegateHelpers.GetActionType(typeArgs!);
                    if (result == null)
                    {
                        throw Error.IncorrectNumberOfTypeArgsForAction(nameof(typeArgs));
                    }

                    return result;
            }
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static bool TryGetActionType(Type[] typeArgs, [NotNullWhen(true)] out Type? actionType)
        {
            if (ValidateTryGetFuncActionArgs(typeArgs) == TryGetFuncActionArgsResult.Valid)
            {
                return (actionType = Compiler.DelegateHelpers.GetActionType(typeArgs)) != null;
            }

            actionType = null;
            return false;
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        public static Type GetDelegateType(params Type[] typeArgs)
        {
            ContractUtils.RequiresNotEmpty(typeArgs, nameof(typeArgs));
            ContractUtils.RequiresNotNullItems(typeArgs, nameof(typeArgs));
            return Compiler.DelegateHelpers.MakeDelegateType(typeArgs);
        }

        internal static ParameterExpression[] CopyParameters(
            IEnumerable<ParameterExpression>? parameters)
        {
            if (parameters is null)
            {
                return [];
            }
            var copy = new List<ParameterExpression>();
            foreach (ParameterExpression? parameter in parameters)
            {
                if (parameter is null)
                {
                    throw new ArgumentNullException(nameof(parameters));
                }
                copy.Add(parameter);
            }
            return copy.ToArray();
        }

        private static void ValidateLambda<TDelegate>(
            ref Expression body,
            ParameterExpression[] parameters)
        {
            Type delegateType = typeof(TDelegate);
            if (delegateType == typeof(MulticastDelegate) ||
                !typeof(MulticastDelegate).IsAssignableFrom(delegateType))
            {
                throw new ArgumentException(
                    "TDelegate must be a concrete delegate type.",
                    nameof(TDelegate));
            }

            MethodInfo invoke = System.Runtime.CompilerServices.DelegateMetadata.GetInvokeMethod<TDelegate>();
            ParameterInfo[] signatureParameters = invoke.GetParameters();
            if (signatureParameters.Length != parameters.Length)
            {
                throw new ArgumentException(
                    "The lambda parameter count does not match the delegate signature.",
                    nameof(parameters));
            }
            for (var index = 0; index < parameters.Length; index++)
            {
                ParameterExpression parameter = parameters[index];
                Type signatureType = signatureParameters[index].ParameterType;
                if (parameter.IsByRef || signatureType.IsByRef)
                {
                    throw new NotSupportedException(
                        "By-reference delegate parameters are not implemented.");
                }
                if (!ExpressionValidation.AreReferenceAssignable(
                    parameter.Type,
                    signatureType))
                {
                    throw new ArgumentException(
                        "A lambda parameter type does not match the delegate signature.",
                        nameof(parameters));
                }
                for (var previous = 0; previous < index; previous++)
                {
                    if (ReferenceEquals(parameters[previous], parameter))
                    {
                        throw new ArgumentException(
                            "Lambda parameters must be unique.",
                            nameof(parameters));
                    }
                }
            }

            Type returnType = invoke.ReturnType;
            ExpressionValidation.RequiresCanRead(body, nameof(body));
            if (returnType != typeof(void) &&
                !ExpressionValidation.AreReferenceAssignable(
                    returnType,
                    body.Type) &&
                !ExpressionValidation.TryQuote(returnType, ref body))
            {
                throw new ArgumentException(
                    "The lambda body does not match the delegate return type.",
                    nameof(body));
            }
        }
    }
}
