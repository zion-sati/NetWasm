// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    public class NewExpression : Expression
    {
        private readonly Type _type;

        internal NewExpression(
            ConstructorInfo constructor,
            Expression[] arguments,
            Type type)
        {
            Constructor = constructor;
            Arguments = arguments.Length == 0
                ? ReadOnlyCollection<Expression>.Empty
                : new ReadOnlyCollection<Expression>(arguments);
            _type = type;
        }

        public sealed override ExpressionType NodeType => ExpressionType.New;

        public override Type Type => _type;

        public ConstructorInfo Constructor { get; }

        public ReadOnlyCollection<Expression> Arguments { get; }

        public ReadOnlyCollection<MemberInfo>? Members => null;

        public NewExpression Update(IEnumerable<Expression>? arguments)
        {
            Expression[] argumentArray = ExpressionValidation.CopyExpressions(
                arguments,
                nameof(arguments));
            return ExpressionValidation.SameExpressions(
                Arguments,
                argumentArray)
                ? this
                : New(Constructor, argumentArray);
        }

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitNew(this);
    }

    public abstract partial class Expression
    {
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
    }
}
