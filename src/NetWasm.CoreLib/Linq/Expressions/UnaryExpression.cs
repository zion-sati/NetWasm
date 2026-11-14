// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: the operator factories, MakeUnary, Update and IsLifted are upstream's. Operator
// lookup by name is bounded as for binary operators: primitive and enum operands declare no operator
// methods, so it finds none; any other operand type throws PNSE, so callers pass the MethodInfo.
// Convert keeps the earlier NetWasm admission (built-in numeric, nullable and reference conversions,
// or an explicit method) instead of upstream's TypeUtils conversion matrix and coercion lookup, and
// ConvertChecked follows it. Throw and the increment/decrement assignments, with their reduction, are
// upstream's. Messages receive the operator method rather than its Name, so building an operator does
// not make member names reachable.

using System.Diagnostics;
using System.Dynamic.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions
{
    public sealed class UnaryExpression : Expression
    {
        internal UnaryExpression(ExpressionType nodeType, Expression expression, Type type, MethodInfo? method)
        {
            Operand = expression;
            Method = method;
            NodeType = nodeType;
            Type = type;
        }

        public sealed override Type Type { get; }

        public sealed override ExpressionType NodeType { get; }

        public Expression Operand { get; }

        public MethodInfo? Method { get; }

        public bool IsLifted
        {
            get
            {
                if (NodeType == ExpressionType.TypeAs || NodeType == ExpressionType.Quote || NodeType == ExpressionType.Throw)
                {
                    return false;
                }
                bool operandIsNullable = Operand.Type.IsNullableType();
                bool resultIsNullable = this.Type.IsNullableType();
                if (Method != null)
                {
                    return (operandIsNullable && !TypeUtils.AreEquivalent(Method.GetParameters()[0].ParameterType, Operand.Type)) ||
                           (resultIsNullable && !TypeUtils.AreEquivalent(Method.ReturnType, this.Type));
                }
                return operandIsNullable || resultIsNullable;
            }
        }

        public bool IsLiftedToNull => IsLifted && this.Type.IsNullableType();

        protected internal override Expression Accept(ExpressionVisitor visitor)
        {
            return visitor.VisitUnary(this);
        }

        public override bool CanReduce
        {
            get
            {
                switch (NodeType)
                {
                    case ExpressionType.PreIncrementAssign:
                    case ExpressionType.PreDecrementAssign:
                    case ExpressionType.PostIncrementAssign:
                    case ExpressionType.PostDecrementAssign:
                        return true;
                }
                return false;
            }
        }

        public override Expression Reduce()
        {
            if (CanReduce)
            {
                switch (Operand.NodeType)
                {
                    case ExpressionType.Index:
                        return ReduceIndex();
                    case ExpressionType.MemberAccess:
                        return ReduceMember();
                    default:
                        Debug.Assert(Operand.NodeType == ExpressionType.Parameter);
                        return ReduceVariable();
                }
            }
            return this;
        }

        private bool IsPrefix
        {
            get { return NodeType == ExpressionType.PreIncrementAssign || NodeType == ExpressionType.PreDecrementAssign; }
        }

        private UnaryExpression FunctionalOp(Expression operand)
        {
            ExpressionType functional;
            if (NodeType == ExpressionType.PreIncrementAssign || NodeType == ExpressionType.PostIncrementAssign)
            {
                functional = ExpressionType.Increment;
            }
            else
            {
                Debug.Assert(NodeType == ExpressionType.PreDecrementAssign || NodeType == ExpressionType.PostDecrementAssign);
                functional = ExpressionType.Decrement;
            }
            return new UnaryExpression(functional, operand, operand.Type, Method);
        }

        private Expression ReduceVariable()
        {
            if (IsPrefix)
            {
                // (op) var
                // ... is reduced into ...
                // var = op(var)
                return Assign(Operand, FunctionalOp(Operand));
            }
            // var (op)
            // ... is reduced into ...
            // temp = var
            // var = op(var)
            // temp
            ParameterExpression temp = Parameter(Operand.Type, name: null);
            return Block(
                new TrueReadOnlyCollection<ParameterExpression>(temp),
                new TrueReadOnlyCollection<Expression>(
                    Assign(temp, Operand),
                    Assign(Operand, FunctionalOp(temp)),
                    temp
                )
            );
        }

        private Expression ReduceMember()
        {
            var member = (MemberExpression)Operand;
            if (member.Expression == null)
            {
                //static member, reduce the same as variable
                return ReduceVariable();
            }
            else
            {
                ParameterExpression temp1 = Parameter(member.Expression.Type, name: null);
                BinaryExpression initTemp1 = Assign(temp1, member.Expression);
                member = MakeMemberAccess(temp1, member.Member);

                if (IsPrefix)
                {
                    // (op) value.member
                    // ... is reduced into ...
                    // temp1 = value
                    // temp1.member = op(temp1.member)
                    return Block(
                        new TrueReadOnlyCollection<ParameterExpression>(temp1),
                        new TrueReadOnlyCollection<Expression>(
                            initTemp1,
                            Assign(member, FunctionalOp(member))
                        )
                    );
                }

                // value.member (op)
                // ... is reduced into ...
                // temp1 = value
                // temp2 = temp1.member
                // temp1.member = op(temp2)
                // temp2
                ParameterExpression temp2 = Parameter(member.Type, name: null);
                return Block(
                    new TrueReadOnlyCollection<ParameterExpression>(temp1, temp2),
                    new TrueReadOnlyCollection<Expression>(
                        initTemp1,
                        Assign(temp2, member),
                        Assign(member, FunctionalOp(temp2)),
                        temp2
                    )
                );
            }
        }

        private Expression ReduceIndex()
        {
            // left[a0, a1, ... aN] (op)
            //
            // ... is reduced into ...
            //
            // tempObj = left
            // tempArg0 = a0
            // ...
            // tempArgN = aN
            // tempValue = tempObj[tempArg0, ... tempArgN]
            // tempObj[tempArg0, ... tempArgN] = op(tempValue)
            // tempValue

            bool prefix = IsPrefix;
            var index = (IndexExpression)Operand;
            int count = index.Arguments.Count;
            var block = new Expression[count + (prefix ? 2 : 4)];
            var temps = new ParameterExpression[count + (prefix ? 1 : 2)];
            var args = new ParameterExpression[count];

            int i = 0;
            temps[i] = Parameter(index.Object!.Type, name: null);
            block[i] = Assign(temps[i], index.Object);
            i++;
            while (i <= count)
            {
                Expression arg = index.Arguments[i - 1];
                args[i - 1] = temps[i] = Parameter(arg.Type, name: null);
                block[i] = Assign(temps[i], arg);
                i++;
            }
            index = MakeIndex(temps[0], index.Indexer, new TrueReadOnlyCollection<Expression>(args));
            if (!prefix)
            {
                ParameterExpression lastTemp = temps[i] = Parameter(index.Type, name: null);
                block[i] = Assign(temps[i], index);
                i++;
                Debug.Assert(i == temps.Length);
                block[i++] = Assign(index, FunctionalOp(lastTemp));
                block[i++] = lastTemp;
            }
            else
            {
                Debug.Assert(i == temps.Length);
                block[i++] = Assign(index, FunctionalOp(index));
            }
            Debug.Assert(i == block.Length);
            return Block(new TrueReadOnlyCollection<ParameterExpression>(temps), new TrueReadOnlyCollection<Expression>(block));
        }

        public UnaryExpression Update(Expression operand)
        {
            if (operand == Operand)
            {
                return this;
            }
            return Expression.MakeUnary(NodeType, operand, Type, Method);
        }
    }

    public abstract partial class Expression
    {
        public static UnaryExpression MakeUnary(ExpressionType unaryType, Expression operand, Type type)
        {
            return MakeUnary(unaryType, operand, type, method: null);
        }

        public static UnaryExpression MakeUnary(ExpressionType unaryType, Expression operand, Type type, MethodInfo? method) =>
            unaryType switch
            {
                ExpressionType.Negate => Negate(operand, method),
                ExpressionType.NegateChecked => NegateChecked(operand, method),
                ExpressionType.Not => Not(operand, method),
                ExpressionType.IsFalse => IsFalse(operand, method),
                ExpressionType.IsTrue => IsTrue(operand, method),
                ExpressionType.OnesComplement => OnesComplement(operand, method),
                ExpressionType.ArrayLength => ArrayLength(operand),
                ExpressionType.Convert => Convert(operand, type, method),
                ExpressionType.ConvertChecked => ConvertChecked(operand, type, method),
                ExpressionType.TypeAs => TypeAs(operand, type),
                ExpressionType.Quote => Quote(operand),
                ExpressionType.UnaryPlus => UnaryPlus(operand, method),
                ExpressionType.Unbox => Unbox(operand, type),
                ExpressionType.Increment => Increment(operand, method),
                ExpressionType.Decrement => Decrement(operand, method),
                ExpressionType.Throw => Throw(operand, type),
                ExpressionType.PreIncrementAssign => PreIncrementAssign(operand, method),
                ExpressionType.PreDecrementAssign => PreDecrementAssign(operand, method),
                ExpressionType.PostIncrementAssign => PostIncrementAssign(operand, method),
                ExpressionType.PostDecrementAssign => PostDecrementAssign(operand, method),
                _ => throw Error.UnhandledUnary(unaryType, nameof(unaryType)),
            };

        private static UnaryExpression GetUserDefinedUnaryOperatorOrThrow(ExpressionType unaryType, string name, Expression operand)
        {
            UnaryExpression? u = GetUserDefinedUnaryOperator(unaryType, name, operand);
            if (u != null)
            {
                ValidateParamsWithOperandsOrThrow(u.Method!.GetParameters()[0].ParameterType, operand.Type, unaryType, name);
                return u;
            }
            throw Error.UnaryOperatorNotDefined(unaryType, operand.Type);
        }

        // NetWasm: upstream finds the operator by name through reflection. Primitive and enum operands
        // declare no operator methods, so that lookup finds none; any other operand type would need
        // runtime member discovery and throws PNSE: pass the operator MethodInfo, as C# does.
        private static UnaryExpression? GetUserDefinedUnaryOperator(ExpressionType unaryType, string name, Expression operand)
        {
            Type nnOperandType = operand.Type.GetNonNullableType();
            if (DeclaresNoOperators(nnOperandType))
            {
                return null;
            }
            // Decimal's own operators, lifted for decimal? as upstream lifts a method found for the underlying type.
            if (nnOperandType == typeof(decimal))
            {
                return DecimalOperators.Unary(name) is { } method
                    ? new UnaryExpression(unaryType, operand, operand.Type == typeof(decimal) ? method.ReturnType : typeof(decimal?), method)
                    : null;
            }
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_OperatorLookup);
        }

        private static UnaryExpression GetMethodBasedUnaryOperator(ExpressionType unaryType, Expression operand, MethodInfo method)
        {
            Debug.Assert(method != null);
            ValidateOperator(method);
            ParameterInfo[] pms = method.GetParameters();
            if (pms.Length != 1)
                throw Error.IncorrectNumberOfMethodCallArguments(method, nameof(method));
            if (ParameterIsAssignable(pms[0], operand.Type))
            {
                ValidateParamsWithOperandsOrThrow(pms[0].ParameterType, operand.Type, unaryType, method);
                return new UnaryExpression(unaryType, operand, method.ReturnType, method);
            }
            // check for lifted call
            if (operand.Type.IsNullableType() &&
                ParameterIsAssignable(pms[0], operand.Type.GetNonNullableType()) &&
                method.ReturnType.IsValueType && !method.ReturnType.IsNullableType())
            {
                return new UnaryExpression(unaryType, operand, method.ReturnType.LiftPrimitiveOrThrow(), method);
            }

            throw Error.OperandTypesDoNotMatchParameters(unaryType, method);
        }

        public static UnaryExpression Negate(Expression expression)
        {
            return Negate(expression, method: null);
        }

        public static UnaryExpression Negate(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsArithmetic() && !expression.Type.IsUnsignedInt())
                {
                    return new UnaryExpression(ExpressionType.Negate, expression, expression.Type, null);
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.Negate, "op_UnaryNegation", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.Negate, expression, method);
        }

        public static UnaryExpression UnaryPlus(Expression expression)
        {
            return UnaryPlus(expression, method: null);
        }

        public static UnaryExpression UnaryPlus(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsArithmetic())
                {
                    return new UnaryExpression(ExpressionType.UnaryPlus, expression, expression.Type, null);
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.UnaryPlus, "op_UnaryPlus", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.UnaryPlus, expression, method);
        }

        public static UnaryExpression NegateChecked(Expression expression)
        {
            return NegateChecked(expression, method: null);
        }

        public static UnaryExpression NegateChecked(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsArithmetic() && !expression.Type.IsUnsignedInt())
                {
                    return new UnaryExpression(ExpressionType.NegateChecked, expression, expression.Type, null);
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.NegateChecked, "op_UnaryNegation", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.NegateChecked, expression, method);
        }

        public static UnaryExpression Not(Expression expression)
        {
            return Not(expression, method: null);
        }

        public static UnaryExpression Not(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsIntegerOrBool())
                {
                    return new UnaryExpression(ExpressionType.Not, expression, expression.Type, null);
                }
                UnaryExpression? u = GetUserDefinedUnaryOperator(ExpressionType.Not, "op_LogicalNot", expression);
                if (u != null)
                {
                    return u;
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.Not, "op_OnesComplement", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.Not, expression, method);
        }

        public static UnaryExpression IsFalse(Expression expression)
        {
            return IsFalse(expression, method: null);
        }

        public static UnaryExpression IsFalse(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsBool())
                {
                    return new UnaryExpression(ExpressionType.IsFalse, expression, expression.Type, null);
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.IsFalse, "op_False", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.IsFalse, expression, method);
        }

        public static UnaryExpression IsTrue(Expression expression)
        {
            return IsTrue(expression, method: null);
        }

        public static UnaryExpression IsTrue(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsBool())
                {
                    return new UnaryExpression(ExpressionType.IsTrue, expression, expression.Type, null);
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.IsTrue, "op_True", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.IsTrue, expression, method);
        }

        public static UnaryExpression OnesComplement(Expression expression)
        {
            return OnesComplement(expression, method: null);
        }

        public static UnaryExpression OnesComplement(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsInteger())
                {
                    return new UnaryExpression(ExpressionType.OnesComplement, expression, expression.Type, null);
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.OnesComplement, "op_OnesComplement", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.OnesComplement, expression, method);
        }

        public static UnaryExpression Convert(Expression expression, Type type) =>
            Convert(expression, type, method: null);

        public static UnaryExpression Convert(
            Expression expression,
            Type type,
            MethodInfo? method)
        {
            ArgumentNullException.ThrowIfNull(expression);
            ArgumentNullException.ThrowIfNull(type);
            ExpressionValidation.RequiresCanRead(
                expression,
                nameof(expression));
            ExpressionValidation.ValidateType(
                type,
                nameof(type),
                allowVoid: false);
            if (type == typeof(void) || type.IsByRef || type.IsPointer)
            {
                throw new NotSupportedException(
                    "This conversion target is outside the NetWasm expression profile.");
            }
            if (method is not null)
            {
                if (IsLiftedConversion(method, expression.Type, type))
                {
                    // As upstream, a method from the underlying types converts the nullable forms, lifted.
                    if (!method.IsStatic || method.ContainsGenericParameters)
                    {
                        throw new ArgumentException("An operator method must be a closed static method.", nameof(method));
                    }
                }
                else
                {
                    ValidateOperatorMethod(method, [expression]);
                    if (method.ReturnType != type)
                    {
                        throw new ArgumentException(
                            "The conversion method return type does not match the target type.",
                            nameof(method));
                    }
                }
            }
            else if (expression.Type != type &&
                !type.IsAssignableFrom(expression.Type) &&
                !expression.Type.IsAssignableFrom(type) &&
                !HasBuiltInNumericOrNullableConversion(expression.Type, type))
            {
                // Decimal's conversions to and from the numeric types, which upstream finds by name.
                method = DecimalOperators.Conversion(expression.Type.GetNonNullableType(), type.GetNonNullableType()) ??
                    throw new NotSupportedException(
                        "The requested built-in conversion is outside the NetWasm expression profile.");
            }
            return new UnaryExpression(
                ExpressionType.Convert,
                expression,
                type,
                method);
        }

        // A conversion method taking the operand's underlying type and returning the target's (or the target),
        // where the operand or the target is nullable: upstream lifts it.
        private static bool IsLiftedConversion(MethodInfo method, Type source, Type target)
        {
            if (!source.IsNullableType() && !target.IsNullableType())
            {
                return false;
            }
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 &&
                TypeUtils.AreReferenceAssignable(parameters[0].ParameterType, source.GetNonNullableType()) &&
                (method.ReturnType == target.GetNonNullableType() || method.ReturnType == target) &&
                !(parameters[0].ParameterType == source && method.ReturnType == target);
        }

        private static bool HasBuiltInNumericOrNullableConversion(Type source, Type target)
        {
            Type from = Nullable.GetUnderlyingType(source) ?? source;
            Type to = Nullable.GetUnderlyingType(target) ?? target;
            return from == to ||
                IsNumeric(from) && IsNumeric(to) &&
                from != typeof(decimal) && to != typeof(decimal);
        }

        public static UnaryExpression ConvertChecked(Expression expression, Type type)
        {
            return ConvertChecked(expression, type, method: null);
        }

        public static UnaryExpression ConvertChecked(Expression expression, Type type, MethodInfo? method)
        {
            // NetWasm: ConvertChecked admits what Convert admits. As upstream, a conversion that is not
            // an identity, primitive or nullable conversion (a reference conversion) builds a Convert
            // node, because it cannot overflow.
            UnaryExpression convert = Convert(expression, type, method);
            if (convert.Method == null && !HasBuiltInNumericOrNullableConversion(expression.Type, type))
            {
                return convert;
            }
            return new UnaryExpression(ExpressionType.ConvertChecked, expression, type, convert.Method);
        }

        public static UnaryExpression Quote(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            if (expression is not LambdaExpression)
            {
                throw new ArgumentException(
                    "Only lambda expressions can be quoted.",
                    nameof(expression));
            }
            return new UnaryExpression(
                ExpressionType.Quote,
                expression,
                ((LambdaExpression)expression).PublicType,
                method: null);
        }

        public static UnaryExpression TypeAs(Expression expression, Type type)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            ArgumentNullException.ThrowIfNull(type);
            TypeUtils.ValidateType(type, nameof(type));
            if (type.IsValueType && !type.IsNullableType())
            {
                throw Error.IncorrectTypeForTypeAs(type, nameof(type));
            }

            return new UnaryExpression(ExpressionType.TypeAs, expression, type, null);
        }

        public static UnaryExpression Unbox(Expression expression, Type type)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            ArgumentNullException.ThrowIfNull(type);
            if (!expression.Type.IsInterface && expression.Type != typeof(object))
            {
                throw Error.InvalidUnboxType(nameof(expression));
            }
            if (!type.IsValueType) throw Error.InvalidUnboxType(nameof(type));
            TypeUtils.ValidateType(type, nameof(type));
            return new UnaryExpression(ExpressionType.Unbox, expression, type, null);
        }

        public static UnaryExpression ArrayLength(Expression array)
        {
            ExpressionUtils.RequiresCanRead(array, nameof(array));
            if (!array.Type.IsSZArray)
            {
                if (!array.Type.IsArray || !typeof(Array).IsAssignableFrom(array.Type))
                {
                    throw Error.ArgumentMustBeArray(nameof(array));
                }

                throw Error.ArgumentMustBeSingleDimensionalArrayType(nameof(array));
            }

            return new UnaryExpression(ExpressionType.ArrayLength, array, typeof(int), null);
        }

        public static UnaryExpression Increment(Expression expression)
        {
            return Increment(expression, method: null);
        }

        public static UnaryExpression Increment(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsArithmetic())
                {
                    return new UnaryExpression(ExpressionType.Increment, expression, expression.Type, null);
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.Increment, "op_Increment", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.Increment, expression, method);
        }

        public static UnaryExpression Decrement(Expression expression)
        {
            return Decrement(expression, method: null);
        }

        public static UnaryExpression Decrement(Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            if (method == null)
            {
                if (expression.Type.IsArithmetic())
                {
                    return new UnaryExpression(ExpressionType.Decrement, expression, expression.Type, null);
                }
                return GetUserDefinedUnaryOperatorOrThrow(ExpressionType.Decrement, "op_Decrement", expression);
            }
            return GetMethodBasedUnaryOperator(ExpressionType.Decrement, expression, method);
        }

        public static UnaryExpression Rethrow()
        {
            return Throw(value: null);
        }
        public static UnaryExpression Rethrow(Type type)
        {
            return Throw(null, type);
        }
        public static UnaryExpression Throw(Expression? value)
        {
            return Throw(value, typeof(void));
        }
        public static UnaryExpression Throw(Expression? value, Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            TypeUtils.ValidateType(type, nameof(type));
            if (value != null)
            {
                ExpressionUtils.RequiresCanRead(value, nameof(value));
                if (value.Type.IsValueType) throw Error.ArgumentMustNotHaveValueType(nameof(value));
            }
            return new UnaryExpression(ExpressionType.Throw, value!, type, null);
        }

        public static UnaryExpression PreIncrementAssign(Expression expression)
        {
            return MakeOpAssignUnary(ExpressionType.PreIncrementAssign, expression, method: null);
        }
        public static UnaryExpression PreIncrementAssign(Expression expression, MethodInfo? method)
        {
            return MakeOpAssignUnary(ExpressionType.PreIncrementAssign, expression, method);
        }
        public static UnaryExpression PreDecrementAssign(Expression expression)
        {
            return MakeOpAssignUnary(ExpressionType.PreDecrementAssign, expression, method: null);
        }
        public static UnaryExpression PreDecrementAssign(Expression expression, MethodInfo? method)
        {
            return MakeOpAssignUnary(ExpressionType.PreDecrementAssign, expression, method);
        }
        public static UnaryExpression PostIncrementAssign(Expression expression)
        {
            return MakeOpAssignUnary(ExpressionType.PostIncrementAssign, expression, method: null);
        }
        public static UnaryExpression PostIncrementAssign(Expression expression, MethodInfo? method)
        {
            return MakeOpAssignUnary(ExpressionType.PostIncrementAssign, expression, method);
        }
        public static UnaryExpression PostDecrementAssign(Expression expression)
        {
            return MakeOpAssignUnary(ExpressionType.PostDecrementAssign, expression, method: null);
        }
        public static UnaryExpression PostDecrementAssign(Expression expression, MethodInfo? method)
        {
            return MakeOpAssignUnary(ExpressionType.PostDecrementAssign, expression, method);
        }

        private static UnaryExpression MakeOpAssignUnary(ExpressionType kind, Expression expression, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(expression, nameof(expression));
            RequiresCanWrite(expression, nameof(expression));

            UnaryExpression result;
            if (method == null)
            {
                if (expression.Type.IsArithmetic())
                {
                    return new UnaryExpression(kind, expression, expression.Type, null);
                }
                string name;
                if (kind == ExpressionType.PreIncrementAssign || kind == ExpressionType.PostIncrementAssign)
                {
                    name = "op_Increment";
                }
                else
                {
                    Debug.Assert(kind == ExpressionType.PreDecrementAssign || kind == ExpressionType.PostDecrementAssign);
                    name = "op_Decrement";
                }
                result = GetUserDefinedUnaryOperatorOrThrow(kind, name, expression);
            }
            else
            {
                result = GetMethodBasedUnaryOperator(kind, expression, method);
            }
            // return type must be assignable back to the operand type
            if (!TypeUtils.AreReferenceAssignable(expression.Type, result.Type))
            {
                throw Error.UserDefinedOpMustHaveValidReturnType(kind, result.Method!);
            }
            return result;
        }
    }
}
