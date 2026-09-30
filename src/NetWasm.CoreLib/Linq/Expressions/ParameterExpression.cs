// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions
{
    public class ParameterExpression : Expression
    {
        private readonly Type _type;

        internal ParameterExpression(Type type, string? name)
        {
            _type = type;
            Name = name;
        }

        public sealed override ExpressionType NodeType => ExpressionType.Parameter;

        public override Type Type => _type;

        public string? Name { get; }

        public bool IsByRef => false;

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitParameter(this);
    }

    public abstract partial class Expression
    {
        public static ParameterExpression Parameter(Type type) =>
            Parameter(type, name: null);

        public static ParameterExpression Parameter(Type type, string? name)
        {
            ValidateParameterType(type, allowByReference: true);
            return new ParameterExpression(type, name);
        }

        public static ParameterExpression Variable(Type type) =>
            Variable(type, name: null);

        public static ParameterExpression Variable(Type type, string? name)
        {
            ValidateParameterType(type, allowByReference: false);
            return new ParameterExpression(type, name);
        }

        private static void ValidateParameterType(
            Type type,
            bool allowByReference)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            ExpressionValidation.ValidateType(
                type,
                nameof(type),
                allowVoid: false);
            if (type == typeof(void))
            {
                throw new ArgumentException(
                    "A parameter cannot have type void.",
                    nameof(type));
            }
            if (type.IsPointer)
            {
                throw new NotSupportedException(
                    "Pointer expression parameters are outside the NetWasm profile.");
            }
            if (type.IsByRef)
            {
                if (!allowByReference)
                {
                    throw new ArgumentException(
                        "A variable cannot have a by-reference type.",
                        nameof(type));
                }
                throw new NotSupportedException(
                    "By-reference expression parameters are not implemented.");
            }
        }
    }
}
