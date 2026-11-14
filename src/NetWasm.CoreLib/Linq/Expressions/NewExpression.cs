// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: one NewExpression class carries the result type, so it also stands in for
// upstream's NewValueTypeExpression. New(Type) cannot look a constructor up by Type: a value type is
// its default value (Constructor is null, as upstream's NewValueTypeExpression; a struct's declared
// parameterless constructor is not found, but C# compiles `new S()` for such a struct to
// New(ConstructorInfo)), and a reference type throws PNSE. The member overloads validate as upstream,
// mapping an accessor MethodInfo to its property through the compiler's accessor descriptor, and
// pass members rather than their names to Error, so construction does not make names reachable.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions
{
    public class NewExpression : Expression, IArgumentProvider
    {
        private readonly Type _type;

        internal NewExpression(
            ConstructorInfo? constructor,
            Expression[] arguments,
            Type type,
            ReadOnlyCollection<MemberInfo>? members = null)
        {
            Constructor = constructor;
            Members = members;
            Arguments = arguments.Length == 0
                ? ReadOnlyCollection<Expression>.Empty
                : new ReadOnlyCollection<Expression>(arguments);
            _type = type;
        }

        public sealed override ExpressionType NodeType => ExpressionType.New;

        public override Type Type => _type;

        public ConstructorInfo? Constructor { get; }

        public ReadOnlyCollection<Expression> Arguments { get; }

        Expression IArgumentProvider.GetArgument(int index) => Arguments[index];

        int IArgumentProvider.ArgumentCount => Arguments.Count;

        public ReadOnlyCollection<MemberInfo>? Members { get; }

        public NewExpression Update(IEnumerable<Expression>? arguments)
        {
            Expression[] argumentArray = ExpressionValidation.CopyExpressions(
                arguments,
                nameof(arguments));
            if (ExpressionValidation.SameExpressions(
                Arguments,
                argumentArray))
            {
                return this;
            }

            return Members != null ? New(Constructor!, argumentArray, Members) : New(Constructor!, argumentArray);
        }

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitNew(this);
    }

    public abstract partial class Expression
    {
        public static NewExpression New(ConstructorInfo constructor) =>
            New(constructor, (IEnumerable<Expression>?)null);

        public static NewExpression New(
            ConstructorInfo constructor,
            params Expression[]? arguments) =>
            New(constructor, (IEnumerable<Expression>?)arguments);

        public static NewExpression New(
            ConstructorInfo constructor,
            IEnumerable<Expression>? arguments)
        {
            ArgumentNullException.ThrowIfNull(constructor);
            if (constructor.IsStatic)
            {
                throw new ArgumentException(
                    "A static constructor cannot create an object.",
                    nameof(constructor));
            }
            Type declaringType = constructor.DeclaringType ??
                throw new InvalidOperationException(
                    "The compiler-generated constructor descriptor has no declaring type.");
            Expression[] argumentArray = ExpressionValidation.CopyExpressions(
                arguments,
                nameof(arguments));
            ExpressionValidation.ValidateArguments(
                constructor,
                argumentArray,
                nameof(arguments));
            return new NewExpression(constructor, argumentArray, declaringType);
        }

        public static NewExpression New(ConstructorInfo constructor, IEnumerable<Expression>? arguments, IEnumerable<MemberInfo>? members)
        {
            ArgumentNullException.ThrowIfNull(constructor);
            if (constructor.DeclaringType is null)
            {
#pragma warning disable CA2208
                throw new ArgumentNullException($"{nameof(constructor)}.{nameof(constructor.DeclaringType)}");
#pragma warning restore CA2208
            }
            TypeUtils.ValidateType(constructor.DeclaringType!, nameof(constructor), allowByRef: true, allowPointer: true);
            ValidateConstructor(constructor, nameof(constructor));
            ReadOnlyCollection<MemberInfo> memberList = members.ToReadOnly();
            ReadOnlyCollection<Expression> argList = arguments.ToReadOnly();
            ValidateNewArgs(constructor, ref argList, ref memberList);
            var argumentArray = new Expression[argList.Count];
            argList.CopyTo(argumentArray, 0);
            return new NewExpression(constructor, argumentArray, constructor.DeclaringType!, memberList);
        }

        public static NewExpression New(ConstructorInfo constructor, IEnumerable<Expression>? arguments, params MemberInfo[]? members)
        {
            return New(constructor, arguments, (IEnumerable<MemberInfo>?)members);
        }

        // NetWasm: C# builds a class's parameterless construction as New(typeof(C)); the compiler rewrites that into
        // this, with C's parameterless constructor and C (which selects a generic class's instantiation).
        internal static NewExpression NewParameterless(RuntimeMethodHandle constructor, RuntimeTypeHandle declaringType) =>
            New((ConstructorInfo)MethodBase.GetMethodFromHandle(constructor, declaringType)!);

        public static NewExpression New(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type == typeof(void))
            {
                throw Error.ArgumentCannotBeOfTypeVoid(nameof(type));
            }
            TypeUtils.ValidateType(type, nameof(type));

            // NetWasm: upstream looks up the parameterless constructor through reflection
            // (GetConstructors), which NetWasm cannot do by Type.
            if (!type.IsValueType)
            {
                throw new PlatformNotSupportedException(SR.PlatformNotSupported_ConstructorLookup);
            }
            return new NewExpression(null, [], type, null);
        }

        private static void ValidateNewArgs(ConstructorInfo constructor, ref ReadOnlyCollection<Expression> arguments, ref ReadOnlyCollection<MemberInfo> members)
        {
            ParameterInfo[] pis;
            if ((pis = constructor.GetParameters()).Length > 0)
            {
                if (arguments.Count != pis.Length)
                {
                    throw Error.IncorrectNumberOfConstructorArguments();
                }
                if (arguments.Count != members.Count)
                {
                    throw Error.IncorrectNumberOfArgumentsForMembers();
                }
                Expression[]? newArguments = null;
                MemberInfo[]? newMembers = null;
                for (int i = 0, n = arguments.Count; i < n; i++)
                {
                    Expression arg = arguments[i];
                    ExpressionUtils.RequiresCanRead(arg, nameof(arguments), i);
                    MemberInfo member = members[i];
                    ContractUtils.RequiresNotNull(member, nameof(members), i);
                    if (!TypeUtils.AreEquivalent(member.DeclaringType, constructor.DeclaringType))
                    {
                        throw Error.ArgumentMemberNotDeclOnType(member, constructor.DeclaringType, nameof(members), i);
                    }
                    Type memberType;
                    ValidateAnonymousTypeMember(ref member, out memberType, nameof(members), i);
                    if (!TypeUtils.AreReferenceAssignable(memberType, arg.Type))
                    {
                        if (!ExpressionUtils.TryQuote(memberType, ref arg))
                        {
                            throw Error.ArgumentTypeDoesNotMatchMember(arg.Type, memberType, nameof(arguments), i);
                        }
                    }
                    ParameterInfo pi = pis[i];
                    Type pType = pi.ParameterType;
                    if (pType.IsByRef)
                    {
                        pType = pType.GetElementType()!;
                    }
                    if (!TypeUtils.AreReferenceAssignable(pType, arg.Type))
                    {
                        if (!ExpressionUtils.TryQuote(pType, ref arg))
                        {
                            throw Error.ExpressionTypeDoesNotMatchConstructorParameter(arg.Type, pType, nameof(arguments), i);
                        }
                    }
                    if (newArguments == null && arg != arguments[i])
                    {
                        newArguments = new Expression[arguments.Count];
                        for (int j = 0; j < i; j++)
                        {
                            newArguments[j] = arguments[j];
                        }
                    }
                    newArguments?[i] = arg;

                    if (newMembers == null && member != members[i])
                    {
                        newMembers = new MemberInfo[members.Count];
                        for (int j = 0; j < i; j++)
                        {
                            newMembers[j] = members[j];
                        }
                    }
                    newMembers?[i] = member;
                }
                if (newArguments != null)
                {
                    arguments = new TrueReadOnlyCollection<Expression>(newArguments);
                }
                if (newMembers != null)
                {
                    members = new TrueReadOnlyCollection<MemberInfo>(newMembers);
                }
            }
            else if (arguments != null && arguments.Count > 0)
            {
                throw Error.IncorrectNumberOfConstructorArguments();
            }
            else if (members != null && members.Count > 0)
            {
                throw Error.IncorrectNumberOfMembersForGivenConstructor();
            }
        }

        private static void ValidateAnonymousTypeMember(ref MemberInfo member, out Type memberType, string paramName, int index)
        {
            if (member is FieldInfo field)
            {
                if (field.IsStatic)
                {
                    throw Error.ArgumentMustBeInstanceMember(paramName, index);
                }
                memberType = field.FieldType;
                return;
            }

            if (member is PropertyInfo pi)
            {
                if (!pi.CanRead)
                {
                    throw Error.PropertyDoesNotHaveGetter(pi, paramName, index);
                }
                if (pi.GetGetMethod()!.IsStatic)
                {
                    throw Error.ArgumentMustBeInstanceMember(paramName, index);
                }
                memberType = pi.PropertyType;
                return;
            }

            if (member is MethodInfo method)
            {
                if (method.IsStatic)
                {
                    throw Error.ArgumentMustBeInstanceMember(paramName, index);
                }

                PropertyInfo prop = GetProperty(method, paramName, index);
                member = prop;
                memberType = prop.PropertyType;
                return;
            }
            throw Error.ArgumentMustBeFieldInfoOrPropertyInfoOrMethod(paramName, index);
        }

        private static void ValidateConstructor(ConstructorInfo constructor, string paramName)
        {
            if (constructor.IsStatic)
                throw Error.NonStaticConstructorRequired(paramName);
        }
    }
}
