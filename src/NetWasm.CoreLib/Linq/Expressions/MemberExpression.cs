// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System.Linq.Expressions
{
    public class MemberExpression : Expression
    {
        private readonly Type _type;

        internal MemberExpression(
            Expression? expression,
            MemberInfo member,
            Type type)
        {
            Expression = expression;
            Member = member;
            _type = type;
        }

        public sealed override ExpressionType NodeType => ExpressionType.MemberAccess;

        public override Type Type => _type;

        public Expression? Expression { get; }

        public MemberInfo Member { get; }

        public MemberExpression Update(Expression? expression) =>
            ReferenceEquals(expression, Expression)
                ? this
                : MakeMemberAccess(expression, Member);

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitMember(this);
    }

    public abstract partial class Expression
    {
        public static MemberExpression Field(
            Expression? expression,
            FieldInfo field)
        {
            ArgumentNullException.ThrowIfNull(field);
            ValidateFieldInstance(expression, field);
            return new MemberExpression(expression, field, field.FieldType);
        }

        public static MemberExpression Property(
            Expression? expression,
            PropertyInfo property)
        {
            ArgumentNullException.ThrowIfNull(property);
            MethodInfo? accessor = property.GetGetMethod(nonPublic: true);
            if (accessor is null)
            {
                accessor = property.GetSetMethod(nonPublic: true);
                if (accessor is null)
                {
                    throw new ArgumentException(
                        "The property has no accessor.",
                        nameof(property));
                }
                if (accessor.GetParameters().Length != 1)
                {
                    throw new ArgumentException(
                        "Indexed properties are outside the NetWasm expression profile.",
                        nameof(property));
                }
            }
            else if (accessor.GetParameters().Length != 0)
            {
                throw new ArgumentException(
                    "Indexed properties are outside the NetWasm expression profile.",
                    nameof(property));
            }
            ExpressionValidation.ValidateInstance(
                expression,
                accessor,
                nameof(expression));
            return new MemberExpression(expression, property, property.PropertyType);
        }

        public static MemberExpression Property(
            Expression? expression,
            MethodInfo propertyAccessor)
        {
            ArgumentNullException.ThrowIfNull(propertyAccessor);
            PropertyInfo property = propertyAccessor.AssociatedProperty ??
                throw new ArgumentException(
                    "The method is not a property accessor.",
                    nameof(propertyAccessor));
            return Property(expression, property);
        }

        public static MemberExpression MakeMemberAccess(
            Expression? expression,
            MemberInfo member)
        {
            ArgumentNullException.ThrowIfNull(member);
            return member switch
            {
                FieldInfo field => Field(expression, field),
                PropertyInfo property => Property(expression, property),
                _ => throw new ArgumentException(
                    "Only field and property descriptors are supported.",
                    nameof(member)),
            };
        }

        private static void ValidateFieldInstance(
            Expression? expression,
            FieldInfo field)
        {
            Type declaringType = field.DeclaringType ??
                throw new InvalidOperationException(
                    "The compiler-generated field descriptor has no declaring type.");
            if (field.IsStatic)
            {
                if (expression is not null)
                {
                    throw new ArgumentException(
                        "A static field cannot have an instance expression.",
                        nameof(expression));
                }
                return;
            }
            if (expression is null)
            {
                throw new ArgumentException(
                    "The field instance does not match its declaring type.",
                    nameof(expression));
            }
            ExpressionValidation.RequiresCanRead(
                expression,
                nameof(expression));
            if (!declaringType.IsAssignableFrom(expression.Type))
            {
                throw new ArgumentException(
                    "The field instance does not match its declaring type.",
                    nameof(expression));
            }
        }
    }
}
