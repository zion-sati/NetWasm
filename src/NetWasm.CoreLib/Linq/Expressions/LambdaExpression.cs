// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        public sealed override ExpressionType NodeType => ExpressionType.Lambda;

        public sealed override Type Type => TypeCore;

        internal abstract Type TypeCore { get; }

        internal abstract Type PublicType { get; }

        public Expression Body => _body;

        public ReadOnlyCollection<ParameterExpression> Parameters => _parameters;

        public string? Name { get; }

        public bool TailCall { get; }

        public Type ReturnType => Type.GetDelegateInvokeMethod().ReturnType;

        public Delegate Compile() => Compile(preferInterpretation: false);

        public Delegate Compile(bool preferInterpretation) => CompileCore();

        internal abstract Delegate CompileCore();

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

    public class Expression<TDelegate> : LambdaExpression
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

        internal sealed override Delegate CompileCore() =>
            (Delegate)(object)Compile()!;

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

            MethodInfo invoke = delegateType.GetDelegateInvokeMethod();
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
