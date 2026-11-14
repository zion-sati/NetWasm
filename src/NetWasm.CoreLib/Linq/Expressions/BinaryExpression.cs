// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: the factories are upstream's, but one BinaryExpression class stands in for its
// Logical/Simple/Method/Assign subclasses (each `new XBinaryExpression(...)` became a constructor call
// with the same node type, result type and method). Compound assignments and the coalescing conversion
// lambda are stored on this class (upstream keeps them on subclasses) and reduce as upstream does. Operator
// lookup by name is bounded (see GetUserDefinedBinaryOperator), string equality without a method
// compares string values with Method left null (see GetEqualityComparisonOperator), user-defined
// conditional logical operators throw PNSE after validation, and Assign and ArrayIndex keep the
// earlier NetWasm bodies. As in Error, messages receive the operator method rather than its Name, so
// building an operator does not make member names reachable.

using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions
{
    public class BinaryExpression : Expression
    {
        private readonly ExpressionType _nodeType;
        private readonly Type _type;

        internal BinaryExpression(
            ExpressionType nodeType,
            Expression left,
            Expression right,
            Type type,
            MethodInfo? method,
            bool comparesStringValues = false,
            LambdaExpression? conversion = null)
        {
            _nodeType = nodeType;
            Left = left;
            Right = right;
            _type = type;
            Method = method;
            ComparesStringValues = comparesStringValues;
            _conversion = conversion;
        }

        private readonly LambdaExpression? _conversion;

        // NetWasm: an Equal or NotEqual of two strings built without a method, which upstream builds
        // with string's operator method.
        internal bool ComparesStringValues { get; }

        public override ExpressionType NodeType => _nodeType;

        public override Type Type => _type;

        public override bool CanReduce
        {
            get
            {
                // Only OpAssignments are reducible.
                return IsOpAssignment(NodeType);
            }
        }

        private static bool IsOpAssignment(ExpressionType op)
        {
            switch (op)
            {
                case ExpressionType.AddAssign:
                case ExpressionType.SubtractAssign:
                case ExpressionType.MultiplyAssign:
                case ExpressionType.AddAssignChecked:
                case ExpressionType.SubtractAssignChecked:
                case ExpressionType.MultiplyAssignChecked:
                case ExpressionType.DivideAssign:
                case ExpressionType.ModuloAssign:
                case ExpressionType.PowerAssign:
                case ExpressionType.AndAssign:
                case ExpressionType.OrAssign:
                case ExpressionType.RightShiftAssign:
                case ExpressionType.LeftShiftAssign:
                case ExpressionType.ExclusiveOrAssign:
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the right operand of the binary operation.
        /// </summary>
        public Expression Right { get; }

        /// <summary>
        /// Gets the left operand of the binary operation.
        /// </summary>
        public Expression Left { get; }

        /// <summary>
        /// Gets the implementing method for the binary operation.
        /// </summary>
        public MethodInfo? Method { get; }

        /// <summary>
        /// Creates a new expression that is like this one, but using the
        /// supplied children. If all of the children are the same, it will
        /// return this expression.
        /// </summary>
        /// <param name="left">The <see cref="Left"/> property of the result.</param>
        /// <param name="conversion">The <see cref="Conversion"/> property of the result.</param>
        /// <param name="right">The <see cref="Right"/> property of the result.</param>
        /// <returns>This expression if no children changed, or an expression with the updated children.</returns>
        public BinaryExpression Update(Expression left, LambdaExpression? conversion, Expression right)
        {
            if (left == Left && right == Right && conversion == Conversion)
            {
                return this;
            }
            if (IsReferenceComparison)
            {
                if (NodeType == ExpressionType.Equal)
                {
                    return Expression.ReferenceEqual(left, right);
                }
                else
                {
                    return Expression.ReferenceNotEqual(left, right);
                }
            }
            return Expression.MakeBinary(NodeType, left, right, IsLiftedToNull, Method, conversion);
        }

        /// <summary>
        /// Reduces the binary expression node to a simpler expression.
        /// If CanReduce returns true, this should return a valid expression.
        /// This method is allowed to return another node which itself
        /// must be reduced.
        /// </summary>
        /// <returns>The reduced expression.</returns>
        public override Expression Reduce()
        {
            // Only reduce OpAssignment expressions.
            if (IsOpAssignment(NodeType))
            {
                return Left.NodeType switch
                {
                    ExpressionType.MemberAccess => ReduceMember(),
                    ExpressionType.Index => ReduceIndex(),
                    _ => ReduceVariable(),
                };
            }
            return this;
        }

        // Return the corresponding Op of an assignment op.
        private static ExpressionType GetBinaryOpFromAssignmentOp(ExpressionType op)
        {
            Debug.Assert(IsOpAssignment(op));
            return op switch
            {
                ExpressionType.AddAssign => ExpressionType.Add,
                ExpressionType.AddAssignChecked => ExpressionType.AddChecked,
                ExpressionType.SubtractAssign => ExpressionType.Subtract,
                ExpressionType.SubtractAssignChecked => ExpressionType.SubtractChecked,
                ExpressionType.MultiplyAssign => ExpressionType.Multiply,
                ExpressionType.MultiplyAssignChecked => ExpressionType.MultiplyChecked,
                ExpressionType.DivideAssign => ExpressionType.Divide,
                ExpressionType.ModuloAssign => ExpressionType.Modulo,
                ExpressionType.PowerAssign => ExpressionType.Power,
                ExpressionType.AndAssign => ExpressionType.And,
                ExpressionType.OrAssign => ExpressionType.Or,
                ExpressionType.RightShiftAssign => ExpressionType.RightShift,
                ExpressionType.LeftShiftAssign => ExpressionType.LeftShift,
                ExpressionType.ExclusiveOrAssign => ExpressionType.ExclusiveOr,
                _ => throw ContractUtils.Unreachable,
            };
        }

        private Expression ReduceVariable()
        {
            // v (op)= r
            // ... is reduced into ...
            // v = v (op) r
            ExpressionType op = GetBinaryOpFromAssignmentOp(NodeType);
            Expression r = Expression.MakeBinary(op, Left, Right, false, Method);
            LambdaExpression? conversion = Conversion;
            if (conversion != null)
            {
                r = Expression.Invoke(conversion, r);
            }
            return Expression.Assign(Left, r);
        }

        private Expression ReduceMember()
        {
            MemberExpression member = (MemberExpression)Left;

            if (member.Expression == null)
            {
                // static member, reduce the same as variable
                return ReduceVariable();
            }
            else
            {
                // left.b (op)= r
                // ... is reduced into ...
                // temp1 = left
                // temp2 = temp1.b (op) r
                // temp1.b = temp2
                // temp2
                ParameterExpression temp1 = Variable(member.Expression.Type, "temp1");

                // 1. temp1 = left
                Expression e1 = Expression.Assign(temp1, member.Expression);

                // 2. temp2 = temp1.b (op) r
                ExpressionType op = GetBinaryOpFromAssignmentOp(NodeType);
                Expression e2 = Expression.MakeBinary(op, Expression.MakeMemberAccess(temp1, member.Member), Right, false, Method);
                LambdaExpression? conversion = Conversion;
                if (conversion != null)
                {
                    e2 = Expression.Invoke(conversion, e2);
                }
                ParameterExpression temp2 = Variable(e2.Type, "temp2");
                e2 = Expression.Assign(temp2, e2);

                // 3. temp1.b = temp2
                Expression e3 = Expression.Assign(Expression.MakeMemberAccess(temp1, member.Member), temp2);

                // 3. temp2
                Expression e4 = temp2;

                return Expression.Block(
                    new TrueReadOnlyCollection<ParameterExpression>(temp1, temp2),
                    new TrueReadOnlyCollection<Expression>(e1, e2, e3, e4)
                );
            }
        }

        private Expression ReduceIndex()
        {
            // left[a0, a1, ... aN] (op)= r
            //
            // ... is reduced into ...
            //
            // tempObj = left
            // tempArg0 = a0
            // ...
            // tempArgN = aN
            // tempValue = tempObj[tempArg0, ... tempArgN] (op) r
            // tempObj[tempArg0, ... tempArgN] = tempValue

            var index = (IndexExpression)Left;

            // NetWasm: lists stand in for upstream's array builders.
            int n = index.Arguments.Count;
            var vars = new List<ParameterExpression>(n + 2);
            var exprs = new List<Expression>(n + 3);

            ParameterExpression tempObj = Expression.Variable(index.Object!.Type, "tempObj");
            vars.Add(tempObj);
            exprs.Add(Expression.Assign(tempObj, index.Object));

            var tempArgs = new List<Expression>(n);
            for (var i = 0; i < n; i++)
            {
                Expression arg = index.Arguments[i];
                ParameterExpression tempArg = Expression.Variable(arg.Type, "tempArg" + i);
                vars.Add(tempArg);
                tempArgs.Add(tempArg);
                exprs.Add(Expression.Assign(tempArg, arg));
            }

            IndexExpression tempIndex = Expression.MakeIndex(tempObj, index.Indexer, tempArgs);

            // tempValue = tempObj[tempArg0, ... tempArgN] (op) r
            ExpressionType binaryOp = GetBinaryOpFromAssignmentOp(NodeType);
            Expression op = Expression.MakeBinary(binaryOp, tempIndex, Right, false, Method);
            LambdaExpression? conversion = Conversion;
            if (conversion != null)
            {
                op = Expression.Invoke(conversion, op);
            }
            ParameterExpression tempValue = Expression.Variable(op.Type, "tempValue");
            vars.Add(tempValue);
            exprs.Add(Expression.Assign(tempValue, op));

            // tempObj[tempArg0, ... tempArgN] = tempValue
            exprs.Add(Expression.Assign(tempIndex, tempValue));

            return Expression.Block(vars, exprs);
        }

        /// <summary>
        /// Gets the type conversion function that is used by a coalescing or compound assignment operation.
        /// </summary>
        public LambdaExpression? Conversion => _conversion;

        /// <summary>
        /// Gets a value that indicates whether the expression tree node represents a lifted call to an operator.
        /// </summary>
        public bool IsLifted
        {
            get
            {
                if (NodeType == ExpressionType.Coalesce || NodeType == ExpressionType.Assign)
                {
                    return false;
                }
                if (Left.Type.IsNullableType())
                {
                    MethodInfo? method = Method;
                    return method == null ||
                        !TypeUtils.AreEquivalent(method.GetParameters()[0].ParameterType.GetNonRefType(), Left.Type);
                }
                return false;
            }
        }

        /// <summary>
        /// Gets a value that indicates whether the expression tree node represents a lifted call to an operator whose return type is lifted to a nullable type.
        /// </summary>
        public bool IsLiftedToNull => IsLifted && Type.IsNullableType();

        /// <summary>
        /// Dispatches to the specific visit method for this node type.
        /// </summary>
        protected internal override Expression Accept(ExpressionVisitor visitor)
        {
            return visitor.VisitBinary(this);
        }

        internal bool IsReferenceComparison
        {
            get
            {
                Type left = Left.Type;
                Type right = Right.Type;
                MethodInfo? method = Method;
                ExpressionType kind = NodeType;

                return (kind == ExpressionType.Equal || kind == ExpressionType.NotEqual) &&
                    method == null && !left.IsValueType && !right.IsValueType && !ComparesStringValues;
            }
        }
    }

    public abstract partial class Expression
    {
        public static BinaryExpression Assign(Expression left, Expression right)
        {
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            TypeUtils.ValidateType(left.Type, nameof(left), allowByRef: true, allowPointer: true);
            TypeUtils.ValidateType(right.Type, nameof(right), allowByRef: true, allowPointer: true);
            if (!TypeUtils.AreReferenceAssignable(left.Type, right.Type))
            {
                throw Error.ExpressionTypeDoesNotMatchAssignment(right.Type, left.Type);
            }
            return new BinaryExpression(ExpressionType.Assign, left, right, left.Type, method: null);
        }

        private static BinaryExpression? GetUserDefinedBinaryOperator(ExpressionType binaryType, string name, Expression left, Expression right, bool liftToNull)
        {
            // try exact match first
            MethodInfo? method = GetUserDefinedBinaryOperator(binaryType, left.Type, right.Type, name);
            if (method != null)
            {
                return new BinaryExpression(binaryType, left, right, method.ReturnType, method);
            }
            // try lifted call
            if (left.Type.IsNullableType() && right.Type.IsNullableType())
            {
                Type nnLeftType = left.Type.GetNonNullableType();
                Type nnRightType = right.Type.GetNonNullableType();
                method = GetUserDefinedBinaryOperator(binaryType, nnLeftType, nnRightType, name);
                if (method != null && method.ReturnType.IsValueType && !method.ReturnType.IsNullableType())
                {
                    if (method.ReturnType != typeof(bool) || liftToNull)
                    {
                        return new BinaryExpression(binaryType, left, right, method.ReturnType.LiftPrimitiveOrThrow(), method);
                    }
                    else
                    {
                        return new BinaryExpression(binaryType, left, right, typeof(bool), method);
                    }
                }
            }
            return null;
        }

        private static BinaryExpression GetMethodBasedBinaryOperator(ExpressionType binaryType, Expression left, Expression right, MethodInfo method, bool liftToNull)
        {
            System.Diagnostics.Debug.Assert(method != null);
            ValidateOperator(method);
            ParameterInfo[] pms = method.GetParameters();
            if (pms.Length != 2)
                throw Error.IncorrectNumberOfMethodCallArguments(method, nameof(method));
            if (ParameterIsAssignable(pms[0], left.Type) && ParameterIsAssignable(pms[1], right.Type))
            {
                ValidateParamsWithOperandsOrThrow(pms[0].ParameterType, left.Type, binaryType, method);
                ValidateParamsWithOperandsOrThrow(pms[1].ParameterType, right.Type, binaryType, method);
                return new BinaryExpression(binaryType, left, right, method.ReturnType, method);
            }
            // check for lifted call
            if (left.Type.IsNullableType() && right.Type.IsNullableType() &&
                ParameterIsAssignable(pms[0], left.Type.GetNonNullableType()) &&
                ParameterIsAssignable(pms[1], right.Type.GetNonNullableType()) &&
                method.ReturnType.IsValueType && !method.ReturnType.IsNullableType())
            {
                if (method.ReturnType != typeof(bool) || liftToNull)
                {
                    return new BinaryExpression(binaryType, left, right, method.ReturnType.LiftPrimitiveOrThrow(), method);
                }
                else
                {
                    return new BinaryExpression(binaryType, left, right, typeof(bool), method);
                }
            }
            throw Error.OperandTypesDoNotMatchParameters(binaryType, method);
        }

        private static BinaryExpression GetUserDefinedBinaryOperatorOrThrow(ExpressionType binaryType, string name, Expression left, Expression right, bool liftToNull)
        {
            BinaryExpression? b = GetUserDefinedBinaryOperator(binaryType, name, left, right, liftToNull);
            if (b != null)
            {
                ParameterInfo[] pis = b.Method!.GetParameters();
                ValidateParamsWithOperandsOrThrow(pis[0].ParameterType, left.Type, binaryType, name);
                ValidateParamsWithOperandsOrThrow(pis[1].ParameterType, right.Type, binaryType, name);
                return b;
            }
            throw Error.BinaryOperatorNotDefined(binaryType, left.Type, right.Type);
        }

        // NetWasm: upstream finds the operator by name through reflection. Primitive, enum and object
        // operands declare no operator methods, so that lookup finds none; equality between reference
        // types finds none either (string equality is handled before the lookup), so it is built-in
        // reference equality, as for a class without equality operators. Any other operand type would
        // need runtime member discovery and throws PNSE: pass the operator MethodInfo, as C# does.
        private static MethodInfo? GetUserDefinedBinaryOperator(ExpressionType binaryType, Type leftType, Type rightType, string name)
        {
            Type nnLeftType = leftType.GetNonNullableType();
            Type nnRightType = rightType.GetNonNullableType();
            if (DeclaresNoOperators(nnLeftType) && DeclaresNoOperators(nnRightType))
            {
                return null;
            }
            // Decimal's operators take two decimals; as upstream's lookup, other operand types (and the nullable forms,
            // which the caller lifts) find none.
            if ((nnLeftType == typeof(decimal) || DeclaresNoOperators(nnLeftType)) &&
                (nnRightType == typeof(decimal) || DeclaresNoOperators(nnRightType)))
            {
                return leftType == typeof(decimal) && rightType == typeof(decimal) ? DecimalOperators.Binary(name) : null;
            }

            if (binaryType is ExpressionType.Equal or ExpressionType.NotEqual &&
                !leftType.IsValueType && !rightType.IsValueType)
            {
                return null;
            }

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_OperatorLookup);
        }

        internal static bool DeclaresNoOperators(Type type) =>
            type.IsEnum || type == typeof(object) || Type.GetTypeCode(type) is
                TypeCode.Boolean or TypeCode.Char or
                TypeCode.SByte or TypeCode.Byte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double;

        // NetWasm: upstream reads Math.Pow from CachedReflectionInfo through reflection; a
        // compiler-generated expression tree supplies the descriptor instead.
        private static MethodInfo? s_Math_Pow_Double_Double;

        private static MethodInfo Math_Pow_Double_Double =>
            s_Math_Pow_Double_Double ??= ((MethodCallExpression)((Expression<Func<double, double, double>>)((x, y) => Math.Pow(x, y))).Body).Method;

        private static bool IsLiftingConditionalLogicalOperator(Type left, Type right, MethodInfo? method, ExpressionType binaryType)
        {
            return right.IsNullableType() &&
                    left.IsNullableType() &&
                    method == null &&
                    (binaryType == ExpressionType.AndAlso || binaryType == ExpressionType.OrElse);
        }

        internal static bool ParameterIsAssignable(ParameterInfo pi, Type argType)
        {
            Type pType = pi.ParameterType;
            if (pType.IsByRef)
                pType = pType.GetElementType()!;
            return TypeUtils.AreReferenceAssignable(pType, argType);
        }

        // NetWasm: callers pass the operator method rather than its name (see the file header).
        private static void ValidateParamsWithOperandsOrThrow(Type paramType, Type operandType, ExpressionType exprType, object name)
        {
            if (paramType.IsNullableType() && !operandType.IsNullableType())
            {
                throw Error.OperandTypesDoNotMatchParameters(exprType, name);
            }
        }

        private static void ValidateOperator(MethodInfo method)
        {
            Debug.Assert(method != null);
            ValidateMethodInfo(method, nameof(method));
            if (!method.IsStatic)
                throw Error.UserDefinedOperatorMustBeStatic(method, nameof(method));
            if (method.ReturnType == typeof(void))
                throw Error.UserDefinedOperatorMustNotBeVoid(method, nameof(method));
        }

        private static bool IsNullComparison(Expression left, Expression right)
        {
            // If we have x==null, x!=null, null==x or null!=x where x is
            // nullable but not null, then this is treated as a call to x.HasValue
            // and is legal even if there is no equality operator defined on the
            // type of x.
            return IsNullConstant(left)
                ? !IsNullConstant(right) && right.Type.IsNullableType()
                : IsNullConstant(right) && left.Type.IsNullableType();
        }

        // Note: this has different meaning than ConstantCheck.IsNull
        // That function attempts to determine if the result of a tree will be
        // null at runtime. This function is used at tree construction time and
        // only looks for a ConstantExpression with a null Value. It can't
        // become "smarter" or that would break tree construction.
        private static bool IsNullConstant(Expression e)
        {
            var c = e as ConstantExpression;
            return c != null && c.Value == null;
        }

        private static void ValidateUserDefinedConditionalLogicOperator(ExpressionType nodeType, Type left, Type right, MethodInfo method)
        {
            ValidateOperator(method);
            ParameterInfo[] pms = method.GetParameters();
            if (pms.Length != 2)
                throw Error.IncorrectNumberOfMethodCallArguments(method, nameof(method));
            if (!ParameterIsAssignable(pms[0], left))
            {
                if (!(left.IsNullableType() && ParameterIsAssignable(pms[0], left.GetNonNullableType())))
                    throw Error.OperandTypesDoNotMatchParameters(nodeType, method);
            }
            if (!ParameterIsAssignable(pms[1], right))
            {
                if (!(right.IsNullableType() && ParameterIsAssignable(pms[1], right.GetNonNullableType())))
                    throw Error.OperandTypesDoNotMatchParameters(nodeType, method);
            }
            if (pms[0].ParameterType != pms[1].ParameterType)
            {
                throw Error.UserDefinedOpMustHaveConsistentTypes(nodeType, method);
            }
            if (method.ReturnType != pms[0].ParameterType)
            {
                throw Error.UserDefinedOpMustHaveConsistentTypes(nodeType, method);
            }
            if (IsValidLiftedConditionalLogicalOperator(left, right, pms))
            {
                left = left.GetNonNullableType();
            }
            Type? declaringType = method.DeclaringType;
            if (declaringType == null)
            {
                throw Error.LogicalOperatorMustHaveBooleanOperators(nodeType, method);
            }
            // NetWasm: upstream finds op_True and op_False by name through reflection.
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_BooleanOperatorLookup);
        }

        private static bool IsValidLiftedConditionalLogicalOperator(Type left, Type right, ParameterInfo[] pms)
        {
            return TypeUtils.AreEquivalent(left, right) &&
                   right.IsNullableType() &&
                   TypeUtils.AreEquivalent(pms[1].ParameterType, right.GetNonNullableType());
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression" />, given the left and right operands, by calling an appropriate factory method.
        /// </summary>
        /// <param name="binaryType">The ExpressionType that specifies the type of binary operation.</param>
        /// <param name="left">An Expression that represents the left operand.</param>
        /// <param name="right">An Expression that represents the right operand.</param>
        /// <returns>The BinaryExpression that results from calling the appropriate factory method.</returns>
        public static BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right)
        {
            return MakeBinary(binaryType, left, right, liftToNull: false, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression" />, given the left and right operands, by calling an appropriate factory method.
        /// </summary>
        /// <param name="binaryType">The ExpressionType that specifies the type of binary operation.</param>
        /// <param name="left">An Expression that represents the left operand.</param>
        /// <param name="right">An Expression that represents the right operand.</param>
        /// <param name="liftToNull">true to set IsLiftedToNull to true; false to set IsLiftedToNull to false.</param>
        /// <param name="method">A MethodInfo that specifies the implementing method.</param>
        /// <returns>The BinaryExpression that results from calling the appropriate factory method.</returns>
        public static BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right, bool liftToNull, MethodInfo? method)
        {
            return MakeBinary(binaryType, left, right, liftToNull, method, conversion: null);
        }

        ///
        /// <summary>
        /// Creates a <see cref="BinaryExpression" />, given the left and right operands, by calling an appropriate factory method.
        /// </summary>
        /// <param name="binaryType">The ExpressionType that specifies the type of binary operation.</param>
        /// <param name="left">An Expression that represents the left operand.</param>
        /// <param name="right">An Expression that represents the right operand.</param>
        /// <param name="liftToNull">true to set IsLiftedToNull to true; false to set IsLiftedToNull to false.</param>
        /// <param name="method">A MethodInfo that specifies the implementing method.</param>
        /// <param name="conversion">A LambdaExpression that represents a type conversion function. This parameter is used if binaryType is Coalesce or compound assignment.</param>
        /// <returns>The BinaryExpression that results from calling the appropriate factory method.</returns>
        public static BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right, bool liftToNull, MethodInfo? method, LambdaExpression? conversion) =>
            binaryType switch
            {
                ExpressionType.Add => Add(left, right, method),
                ExpressionType.AddChecked => AddChecked(left, right, method),
                ExpressionType.Subtract => Subtract(left, right, method),
                ExpressionType.SubtractChecked => SubtractChecked(left, right, method),
                ExpressionType.Multiply => Multiply(left, right, method),
                ExpressionType.MultiplyChecked => MultiplyChecked(left, right, method),
                ExpressionType.Divide => Divide(left, right, method),
                ExpressionType.Modulo => Modulo(left, right, method),
                ExpressionType.Power => Power(left, right, method),
                ExpressionType.And => And(left, right, method),
                ExpressionType.AndAlso => AndAlso(left, right, method),
                ExpressionType.Or => Or(left, right, method),
                ExpressionType.OrElse => OrElse(left, right, method),
                ExpressionType.LessThan => LessThan(left, right, liftToNull, method),
                ExpressionType.LessThanOrEqual => LessThanOrEqual(left, right, liftToNull, method),
                ExpressionType.GreaterThan => GreaterThan(left, right, liftToNull, method),
                ExpressionType.GreaterThanOrEqual => GreaterThanOrEqual(left, right, liftToNull, method),
                ExpressionType.Equal => Equal(left, right, liftToNull, method),
                ExpressionType.NotEqual => NotEqual(left, right, liftToNull, method),
                ExpressionType.ExclusiveOr => ExclusiveOr(left, right, method),
                ExpressionType.Coalesce => Coalesce(left, right, conversion),
                ExpressionType.ArrayIndex => ArrayIndex(left, right),
                ExpressionType.RightShift => RightShift(left, right, method),
                ExpressionType.LeftShift => LeftShift(left, right, method),
                ExpressionType.Assign => Assign(left, right),
                ExpressionType.AddAssign => AddAssign(left, right, method, conversion),
                ExpressionType.AndAssign => AndAssign(left, right, method, conversion),
                ExpressionType.DivideAssign => DivideAssign(left, right, method, conversion),
                ExpressionType.ExclusiveOrAssign => ExclusiveOrAssign(left, right, method, conversion),
                ExpressionType.LeftShiftAssign => LeftShiftAssign(left, right, method, conversion),
                ExpressionType.ModuloAssign => ModuloAssign(left, right, method, conversion),
                ExpressionType.MultiplyAssign => MultiplyAssign(left, right, method, conversion),
                ExpressionType.OrAssign => OrAssign(left, right, method, conversion),
                ExpressionType.PowerAssign => PowerAssign(left, right, method, conversion),
                ExpressionType.RightShiftAssign => RightShiftAssign(left, right, method, conversion),
                ExpressionType.SubtractAssign => SubtractAssign(left, right, method, conversion),
                ExpressionType.AddAssignChecked => AddAssignChecked(left, right, method, conversion),
                ExpressionType.SubtractAssignChecked => SubtractAssignChecked(left, right, method, conversion),
                ExpressionType.MultiplyAssignChecked => MultiplyAssignChecked(left, right, method, conversion),
                _ => throw Error.UnhandledBinary(binaryType, nameof(binaryType)),
            };

        #region Equality Operators

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an equality comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Equal"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Equal(Expression left, Expression right)
        {
            return Equal(left, right, liftToNull: false, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an equality comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="liftToNull">true to set IsLiftedToNull to true; false to set IsLiftedToNull to false.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Equal"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.IsLiftedToNull"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Equal(Expression left, Expression right, bool liftToNull, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                return GetEqualityComparisonOperator(ExpressionType.Equal, "op_Equality", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Equal, left, right, method, liftToNull);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a reference equality comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Equal"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression ReferenceEqual(Expression left, Expression right)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (TypeUtils.HasReferenceEquality(left.Type, right.Type))
            {
                return new BinaryExpression(ExpressionType.Equal, left, right, typeof(bool), method: null);
            }
            throw Error.ReferenceEqualityNotDefined(left.Type, right.Type);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an inequality comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.NotEqual"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression NotEqual(Expression left, Expression right)
        {
            return NotEqual(left, right, liftToNull: false, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an inequality comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="liftToNull">true to set IsLiftedToNull to true; false to set IsLiftedToNull to false.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.NotEqual"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.IsLiftedToNull"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression NotEqual(Expression left, Expression right, bool liftToNull, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                return GetEqualityComparisonOperator(ExpressionType.NotEqual, "op_Inequality", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.NotEqual, left, right, method, liftToNull);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a reference inequality comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.NotEqual"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression ReferenceNotEqual(Expression left, Expression right)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (TypeUtils.HasReferenceEquality(left.Type, right.Type))
            {
                return new BinaryExpression(ExpressionType.NotEqual, left, right, typeof(bool), method: null);
            }
            throw Error.ReferenceEqualityNotDefined(left.Type, right.Type);
        }

        private static BinaryExpression GetEqualityComparisonOperator(ExpressionType binaryType, string opName, Expression left, Expression right, bool liftToNull)
        {
            // known comparison - numeric types, bools, object, enums
            if (left.Type == right.Type && (left.Type.IsNumeric() ||
                left.Type == typeof(object) ||
                left.Type.IsBool() ||
                left.Type.GetNonNullableType().IsEnum))
            {
                if (left.Type.IsNullableType() && liftToNull)
                {
                    return new BinaryExpression(binaryType, left, right, typeof(bool?), method: null);
                }
                else
                {
                    return new BinaryExpression(binaryType, left, right, typeof(bool), method: null);
                }
            }
            // NetWasm: upstream's lookup finds string's operator method here. NetWasm cannot find it by
            // name, and C# compiles string == and != in expression trees without it, so the node keeps
            // Method null and the interpreter compares the string values, as the operator does.
            if (left.Type == typeof(string) && right.Type == typeof(string))
            {
                return new BinaryExpression(binaryType, left, right, typeof(bool), method: null, comparesStringValues: true);
            }
            // look for user defined operator
            BinaryExpression? b = GetUserDefinedBinaryOperator(binaryType, opName, left, right, liftToNull);
            if (b != null)
            {
                return b;
            }
            if (TypeUtils.HasBuiltInEqualityOperator(left.Type, right.Type) || IsNullComparison(left, right))
            {
                if (left.Type.IsNullableType() && liftToNull)
                {
                    return new BinaryExpression(binaryType, left, right, typeof(bool?), method: null);
                }
                else
                {
                    return new BinaryExpression(binaryType, left, right, typeof(bool), method: null);
                }
            }
            throw Error.BinaryOperatorNotDefined(binaryType, left.Type, right.Type);
        }

        #endregion

        #region Comparison Expressions

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a "greater than" numeric comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.GreaterThan"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression GreaterThan(Expression left, Expression right)
        {
            return GreaterThan(left, right, liftToNull: false, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a "greater than" numeric comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="liftToNull">true to set IsLiftedToNull to true; false to set IsLiftedToNull to false.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.GreaterThan"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.IsLiftedToNull"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression GreaterThan(Expression left, Expression right, bool liftToNull, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                return GetComparisonOperator(ExpressionType.GreaterThan, "op_GreaterThan", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.GreaterThan, left, right, method, liftToNull);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a "less than" numeric comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LessThan"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>

        public static BinaryExpression LessThan(Expression left, Expression right)
        {
            return LessThan(left, right, liftToNull: false, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a "less than" numeric comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="liftToNull">true to set IsLiftedToNull to true; false to set IsLiftedToNull to false.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LessThan"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.IsLiftedToNull"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression LessThan(Expression left, Expression right, bool liftToNull, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                return GetComparisonOperator(ExpressionType.LessThan, "op_LessThan", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.LessThan, left, right, method, liftToNull);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a "greater than or equal" numeric comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.GreaterThanOrEqual"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression GreaterThanOrEqual(Expression left, Expression right)
        {
            return GreaterThanOrEqual(left, right, liftToNull: false, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a "greater than or equal" numeric comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="liftToNull">true to set IsLiftedToNull to true; false to set IsLiftedToNull to false.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.GreaterThanOrEqual"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.IsLiftedToNull"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression GreaterThanOrEqual(Expression left, Expression right, bool liftToNull, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                return GetComparisonOperator(ExpressionType.GreaterThanOrEqual, "op_GreaterThanOrEqual", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.GreaterThanOrEqual, left, right, method, liftToNull);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a "less than or equal" numeric comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LessThanOrEqual"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression LessThanOrEqual(Expression left, Expression right)
        {
            return LessThanOrEqual(left, right, liftToNull: false, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a "less than or equal" numeric comparison.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="liftToNull">true to set IsLiftedToNull to true; false to set IsLiftedToNull to false.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LessThanOrEqual"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.IsLiftedToNull"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression LessThanOrEqual(Expression left, Expression right, bool liftToNull, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                return GetComparisonOperator(ExpressionType.LessThanOrEqual, "op_LessThanOrEqual", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.LessThanOrEqual, left, right, method, liftToNull);
        }

        private static BinaryExpression GetComparisonOperator(ExpressionType binaryType, string opName, Expression left, Expression right, bool liftToNull)
        {
            if (left.Type == right.Type && left.Type.IsNumeric())
            {
                if (left.Type.IsNullableType() && liftToNull)
                {
                    return new BinaryExpression(binaryType, left, right, typeof(bool?), method: null);
                }
                else
                {
                    return new BinaryExpression(binaryType, left, right, typeof(bool), method: null);
                }
            }
            return GetUserDefinedBinaryOperatorOrThrow(binaryType, opName, left, right, liftToNull);
        }

        #endregion

        #region Boolean Expressions

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a conditional AND operation that evaluates the second operand only if it has to.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AndAlso"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression AndAlso(Expression left, Expression right)
        {
            return AndAlso(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a conditional AND operation that evaluates the second operand only if it has to.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AndAlso"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression AndAlso(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            Type returnType;
            if (method == null)
            {
                if (left.Type == right.Type)
                {
                    if (left.Type == typeof(bool))
                    {
                        return new BinaryExpression(ExpressionType.AndAlso, left, right, typeof(bool), method: null);
                    }
                    else if (left.Type == typeof(bool?))
                    {
                        return new BinaryExpression(ExpressionType.AndAlso, left, right, left.Type, method: null);
                    }
                }
                method = GetUserDefinedBinaryOperator(ExpressionType.AndAlso, left.Type, right.Type, "op_BitwiseAnd");
                if (method != null)
                {
                    ValidateUserDefinedConditionalLogicOperator(ExpressionType.AndAlso, left.Type, right.Type, method);
                    returnType = (left.Type.IsNullableType() && TypeUtils.AreEquivalent(method.ReturnType, left.Type.GetNonNullableType())) ? left.Type : method.ReturnType;
                    return new BinaryExpression(ExpressionType.AndAlso, left, right, returnType, method);
                }
                throw Error.BinaryOperatorNotDefined(ExpressionType.AndAlso, left.Type, right.Type);
            }
            ValidateUserDefinedConditionalLogicOperator(ExpressionType.AndAlso, left.Type, right.Type, method);
            returnType = (left.Type.IsNullableType() && TypeUtils.AreEquivalent(method.ReturnType, left.Type.GetNonNullableType())) ? left.Type : method.ReturnType;
            return new BinaryExpression(ExpressionType.AndAlso, left, right, returnType, method);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a conditional OR operation that evaluates the second operand only if it has to.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.OrElse"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression OrElse(Expression left, Expression right)
        {
            return OrElse(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a conditional OR operation that evaluates the second operand only if it has to.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.OrElse"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression OrElse(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            Type returnType;
            if (method == null)
            {
                if (left.Type == right.Type)
                {
                    if (left.Type == typeof(bool))
                    {
                        return new BinaryExpression(ExpressionType.OrElse, left, right, typeof(bool), method: null);
                    }
                    else if (left.Type == typeof(bool?))
                    {
                        return new BinaryExpression(ExpressionType.OrElse, left, right, left.Type, method: null);
                    }
                }
                method = GetUserDefinedBinaryOperator(ExpressionType.OrElse, left.Type, right.Type, "op_BitwiseOr");
                if (method != null)
                {
                    ValidateUserDefinedConditionalLogicOperator(ExpressionType.OrElse, left.Type, right.Type, method);
                    returnType = (left.Type.IsNullableType() && method.ReturnType == left.Type.GetNonNullableType()) ? left.Type : method.ReturnType;
                    return new BinaryExpression(ExpressionType.OrElse, left, right, returnType, method);
                }
                throw Error.BinaryOperatorNotDefined(ExpressionType.OrElse, left.Type, right.Type);
            }
            ValidateUserDefinedConditionalLogicOperator(ExpressionType.OrElse, left.Type, right.Type, method);
            returnType = (left.Type.IsNullableType() && method.ReturnType == left.Type.GetNonNullableType()) ? left.Type : method.ReturnType;
            return new BinaryExpression(ExpressionType.OrElse, left, right, returnType, method);
        }

        #endregion

        #region Coalescing Expressions

        /// <summary>
        /// Creates a <see cref="BinaryExpression" /> that represents a coalescing operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression" /> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Coalesce"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Coalesce(Expression left, Expression right)
        {
            return Coalesce(left, right, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression" /> that represents a coalescing operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="conversion">A LambdaExpression to set the Conversion property equal to.</param>
        /// <returns>A <see cref="BinaryExpression" /> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Coalesce"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/> and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Coalesce(Expression left, Expression right, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));

            if (conversion == null)
            {
                Type resultType = ValidateCoalesceArgTypes(left.Type, right.Type);
                return new BinaryExpression(ExpressionType.Coalesce, left, right, resultType, method: null);
            }

            if (left.Type.IsValueType && !left.Type.IsNullableType())
            {
                throw Error.CoalesceUsedOnNonNullType();
            }

            Type delegateType = conversion.Type;
            Debug.Assert(typeof(System.MulticastDelegate).IsAssignableFrom(delegateType) && delegateType != typeof(System.MulticastDelegate));
            // NetWasm: the conversion lambda's type was named when compiling, which gives its Invoke.
            MethodInfo method = conversion.InvokeMethod;
            if (method.ReturnType == typeof(void))
            {
                throw Error.UserDefinedOperatorMustNotBeVoid(conversion, nameof(conversion));
            }
            ParameterInfo[] pms = method.GetParameters();
            Debug.Assert(pms.Length == conversion.Parameters.Count);
            if (pms.Length != 1)
            {
                throw Error.IncorrectNumberOfMethodCallArguments(conversion, nameof(conversion));
            }
            // The return type must match exactly.
            // We could weaken this restriction and
            // say that the return type must be assignable to from
            // the return type of the lambda.
            if (!TypeUtils.AreEquivalent(method.ReturnType, right.Type))
            {
                throw Error.OperandTypesDoNotMatchParameters(ExpressionType.Coalesce, conversion);
            }
            // The parameter of the conversion lambda must either be assignable
            // from the erased or unerased type of the left hand side.
            if (!ParameterIsAssignable(pms[0], left.Type.GetNonNullableType()) &&
                !ParameterIsAssignable(pms[0], left.Type))
            {
                throw Error.OperandTypesDoNotMatchParameters(ExpressionType.Coalesce, conversion);
            }
            return new BinaryExpression(ExpressionType.Coalesce, left, right, right.Type, method: null, conversion: conversion);
        }

        private static Type ValidateCoalesceArgTypes(Type left, Type right)
        {
            Type leftStripped = left.GetNonNullableType();
            if (left.IsValueType && !left.IsNullableType())
            {
                throw Error.CoalesceUsedOnNonNullType();
            }
            else if (left.IsNullableType() && right.IsImplicitlyConvertibleTo(leftStripped))
            {
                return leftStripped;
            }
            else if (right.IsImplicitlyConvertibleTo(left))
            {
                return left;
            }
            else if (leftStripped.IsImplicitlyConvertibleTo(right))
            {
                return right;
            }
            else
            {
                throw Error.ArgumentTypesMustMatch();
            }
        }

        #endregion

        #region Compound Assignment Expressions

        // NetWasm: dotnet/runtime 474ed96's compound assignment factories and their helpers. Each upstream subclass
        // constructor became the single BinaryExpression constructor carrying the conversion, and messages receive the
        // operator method rather than its name, as elsewhere in this file.

        private static BinaryExpression GetMethodBasedAssignOperator(ExpressionType binaryType, Expression left, Expression right, MethodInfo method, LambdaExpression? conversion, bool liftToNull)
        {
            BinaryExpression b = GetMethodBasedBinaryOperator(binaryType, left, right, method, liftToNull);
            if (conversion == null)
            {
                // return type must be assignable back to the left type
                if (!TypeUtils.AreReferenceAssignable(left.Type, b.Type))
                {
                    throw Error.UserDefinedOpMustHaveValidReturnType(binaryType, b.Method!);
                }
            }
            else
            {
                // add the conversion to the result
                ValidateOpAssignConversionLambda(conversion, b.Left, b.Method!, b.NodeType);
                b = new BinaryExpression(b.NodeType, b.Left, b.Right, b.Left.Type, b.Method!, conversion: conversion);
            }
            return b;
        }

        private static BinaryExpression GetUserDefinedAssignOperatorOrThrow(ExpressionType binaryType, string name, Expression left, Expression right, LambdaExpression? conversion, bool liftToNull)
        {
            BinaryExpression b = GetUserDefinedBinaryOperatorOrThrow(binaryType, name, left, right, liftToNull);
            if (conversion == null)
            {
                // return type must be assignable back to the left type
                if (!TypeUtils.AreReferenceAssignable(left.Type, b.Type))
                {
                    throw Error.UserDefinedOpMustHaveValidReturnType(binaryType, b.Method!);
                }
            }
            else
            {
                // add the conversion to the result
                ValidateOpAssignConversionLambda(conversion, b.Left, b.Method!, b.NodeType);
                b = new BinaryExpression(b.NodeType, b.Left, b.Right, b.Left.Type, b.Method!, conversion: conversion);
            }
            return b;
        }

        private static void ValidateOpAssignConversionLambda(LambdaExpression conversion, Expression left, MethodInfo method, ExpressionType nodeType)
        {
            Type delegateType = conversion.Type;
            Debug.Assert(typeof(System.MulticastDelegate).IsAssignableFrom(delegateType) && delegateType != typeof(System.MulticastDelegate));
            // NetWasm: the conversion lambda's type was named when compiling, which gives its Invoke.
            MethodInfo mi = conversion.InvokeMethod;
            ParameterInfo[] pms = mi.GetParameters();
            Debug.Assert(pms.Length == conversion.Parameters.Count);
            if (pms.Length != 1)
            {
                throw Error.IncorrectNumberOfMethodCallArguments(conversion, nameof(conversion));
            }
            if (!TypeUtils.AreEquivalent(mi.ReturnType, left.Type))
            {
                throw Error.OperandTypesDoNotMatchParameters(nodeType, conversion);
            }
            Debug.Assert(method != null);
            // The parameter type of conversion lambda must be the same as the return type of the overload method
            if (!TypeUtils.AreEquivalent(pms[0].ParameterType, method.ReturnType))
            {
                throw Error.OverloadOperatorTypeDoesNotMatchConversionType(nodeType, conversion);
            }
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an addition assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AddAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression AddAssign(Expression left, Expression right)
        {
            return AddAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an addition assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AddAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression AddAssign(Expression left, Expression right, MethodInfo? method)
        {
            return AddAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an addition assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AddAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression AddAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.AddAssign, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.AddAssign, "op_Addition", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.AddAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an addition assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to
        /// <see cref="ExpressionType.AddAssignChecked"/> and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/>
        /// properties set to the specified values.
        /// </returns>
        public static BinaryExpression AddAssignChecked(Expression left, Expression right)
        {
            return AddAssignChecked(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an addition assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AddAssignChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression AddAssignChecked(Expression left, Expression right, MethodInfo? method)
        {
            return AddAssignChecked(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an addition assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AddAssignChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression AddAssignChecked(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));

            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.AddAssignChecked, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.AddAssignChecked, "op_Addition", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.AddAssignChecked, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a subtraction assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.SubtractAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression SubtractAssign(Expression left, Expression right)
        {
            return SubtractAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a subtraction assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.SubtractAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression SubtractAssign(Expression left, Expression right, MethodInfo? method)
        {
            return SubtractAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a subtraction assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.SubtractAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression SubtractAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.SubtractAssign, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.SubtractAssign, "op_Subtraction", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.SubtractAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a subtraction assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.SubtractAssignChecked"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression SubtractAssignChecked(Expression left, Expression right)
        {
            return SubtractAssignChecked(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a subtraction assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.SubtractAssignChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression SubtractAssignChecked(Expression left, Expression right, MethodInfo? method)
        {
            return SubtractAssignChecked(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a subtraction assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.SubtractAssignChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression SubtractAssignChecked(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.SubtractAssignChecked, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.SubtractAssignChecked, "op_Subtraction", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.SubtractAssignChecked, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a multiplication assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.MultiplyAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression MultiplyAssign(Expression left, Expression right)
        {
            return MultiplyAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a multiplication assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.MultiplyAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression MultiplyAssign(Expression left, Expression right, MethodInfo? method)
        {
            return MultiplyAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a multiplication assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.MultiplyAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression MultiplyAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.MultiplyAssign, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.MultiplyAssign, "op_Multiply", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.MultiplyAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a multiplication assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.MultiplyAssignChecked"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression MultiplyAssignChecked(Expression left, Expression right)
        {
            return MultiplyAssignChecked(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a multiplication assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.MultiplyAssignChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression MultiplyAssignChecked(Expression left, Expression right, MethodInfo? method)
        {
            return MultiplyAssignChecked(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a multiplication assignment operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.MultiplyAssignChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression MultiplyAssignChecked(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.MultiplyAssignChecked, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.MultiplyAssignChecked, "op_Multiply", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.MultiplyAssignChecked, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a division assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.DivideAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression DivideAssign(Expression left, Expression right)
        {
            return DivideAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a division assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.DivideAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression DivideAssign(Expression left, Expression right, MethodInfo? method)
        {
            return DivideAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a division assignment operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.DivideAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression DivideAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.DivideAssign, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.DivideAssign, "op_Division", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.DivideAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a remainder assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.ModuloAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression ModuloAssign(Expression left, Expression right)
        {
            return ModuloAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a remainder assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.ModuloAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression ModuloAssign(Expression left, Expression right, MethodInfo? method)
        {
            return ModuloAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a remainder assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.ModuloAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression ModuloAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.ModuloAssign, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.ModuloAssign, "op_Modulus", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.ModuloAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents raising an expression to a power and assigning the result back to the expression.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.PowerAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression PowerAssign(Expression left, Expression right)
        {
            return PowerAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents raising an expression to a power and assigning the result back to the expression.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.PowerAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression PowerAssign(Expression left, Expression right, MethodInfo? method)
        {
            return PowerAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents raising an expression to a power and assigning the result back to the expression.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.PowerAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression PowerAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                method = Math_Pow_Double_Double;
                if (method == null)
                {
                    throw Error.BinaryOperatorNotDefined(ExpressionType.PowerAssign, left.Type, right.Type);
                }
            }
            return GetMethodBasedAssignOperator(ExpressionType.PowerAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise left-shift assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LeftShiftAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression LeftShiftAssign(Expression left, Expression right)
        {
            return LeftShiftAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise left-shift assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LeftShiftAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression LeftShiftAssign(Expression left, Expression right, MethodInfo? method)
        {
            return LeftShiftAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise left-shift assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LeftShiftAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression LeftShiftAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (IsSimpleShift(left.Type, right.Type))
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    Type resultType = GetResultTypeOfShift(left.Type, right.Type);
                    return new BinaryExpression(ExpressionType.LeftShiftAssign, left, right, resultType, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.LeftShiftAssign, "op_LeftShift", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.LeftShiftAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise right-shift assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.RightShiftAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression RightShiftAssign(Expression left, Expression right)
        {
            return RightShiftAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise right-shift assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.RightShiftAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression RightShiftAssign(Expression left, Expression right, MethodInfo? method)
        {
            return RightShiftAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise right-shift assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.RightShiftAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression RightShiftAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (IsSimpleShift(left.Type, right.Type))
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    Type resultType = GetResultTypeOfShift(left.Type, right.Type);
                    return new BinaryExpression(ExpressionType.RightShiftAssign, left, right, resultType, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.RightShiftAssign, "op_RightShift", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.RightShiftAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise AND assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AndAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression AndAssign(Expression left, Expression right)
        {
            return AndAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise AND assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AndAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression AndAssign(Expression left, Expression right, MethodInfo? method)
        {
            return AndAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise AND assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AndAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression AndAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsIntegerOrBool())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.AndAssign, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.AndAssign, "op_BitwiseAnd", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.AndAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise OR assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.OrAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression OrAssign(Expression left, Expression right)
        {
            return OrAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise OR assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.OrAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression OrAssign(Expression left, Expression right, MethodInfo? method)
        {
            return OrAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise OR assignment operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.OrAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression OrAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsIntegerOrBool())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.OrAssign, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.OrAssign, "op_BitwiseOr", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.OrAssign, left, right, method, conversion, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise or logical XOR assignment operation, using op_ExclusiveOr for user-defined types.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.ExclusiveOrAssign"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression ExclusiveOrAssign(Expression left, Expression right)
        {
            return ExclusiveOrAssign(left, right, method: null, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise or logical XOR assignment operation, using op_ExclusiveOr for user-defined types.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.ExclusiveOrAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression ExclusiveOrAssign(Expression left, Expression right, MethodInfo? method)
        {
            return ExclusiveOrAssign(left, right, method, conversion: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise or logical XOR assignment operation, using op_ExclusiveOr for user-defined types.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <param name="conversion">A <see cref="LambdaExpression"/> to set the <see cref="BinaryExpression.Conversion"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.ExclusiveOrAssign"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, <see cref="BinaryExpression.Method"/>,
        /// and <see cref="BinaryExpression.Conversion"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression ExclusiveOrAssign(Expression left, Expression right, MethodInfo? method, LambdaExpression? conversion)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            RequiresCanWrite(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsIntegerOrBool())
                {
                    // conversion is not supported for binary ops on arithmetic types without operator overloading
                    if (conversion != null)
                    {
                        throw Error.ConversionIsNotSupportedForArithmeticTypes();
                    }
                    return new BinaryExpression(ExpressionType.ExclusiveOrAssign, left, right, left.Type, method: null);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.ExclusiveOrAssign, "op_ExclusiveOr", left, right, conversion, liftToNull: true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.ExclusiveOrAssign, left, right, method, conversion, liftToNull: true);
        }

        #endregion

        #region Arithmetic Expressions

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic addition operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Add"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Add(Expression left, Expression right)
        {
            return Add(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic addition operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Add"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Add(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    return new BinaryExpression(ExpressionType.Add, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Add, "op_Addition", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Add, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic addition operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AddChecked"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression AddChecked(Expression left, Expression right)
        {
            return AddChecked(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic addition operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.AddChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression AddChecked(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    return new BinaryExpression(ExpressionType.AddChecked, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.AddChecked, "op_Addition", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.AddChecked, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic subtraction operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Subtract"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Subtract(Expression left, Expression right)
        {
            return Subtract(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic subtraction operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Subtract"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Subtract(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    return new BinaryExpression(ExpressionType.Subtract, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Subtract, "op_Subtraction", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Subtract, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic subtraction operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.SubtractChecked"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression SubtractChecked(Expression left, Expression right)
        {
            return SubtractChecked(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic subtraction operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.SubtractChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression SubtractChecked(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    return new BinaryExpression(ExpressionType.SubtractChecked, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.SubtractChecked, "op_Subtraction", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.SubtractChecked, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic division operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Divide"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Divide(Expression left, Expression right)
        {
            return Divide(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic division operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Divide"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Divide(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    return new BinaryExpression(ExpressionType.Divide, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Divide, "op_Division", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Divide, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic remainder operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Modulo"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Modulo(Expression left, Expression right)
        {
            return Modulo(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic remainder operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Modulo"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Modulo(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    return new BinaryExpression(ExpressionType.Modulo, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Modulo, "op_Modulus", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Modulo, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic multiplication operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Multiply"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Multiply(Expression left, Expression right)
        {
            return Multiply(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic multiplication operation that does not have overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Multiply"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Multiply(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    return new BinaryExpression(ExpressionType.Multiply, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Multiply, "op_Multiply", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Multiply, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic multiplication operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.MultiplyChecked"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression MultiplyChecked(Expression left, Expression right)
        {
            return MultiplyChecked(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an arithmetic multiplication operation that has overflow checking.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.MultiplyChecked"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression MultiplyChecked(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    return new BinaryExpression(ExpressionType.MultiplyChecked, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.MultiplyChecked, "op_Multiply", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.MultiplyChecked, left, right, method, liftToNull: true);
        }

        private static bool IsSimpleShift(Type left, Type right)
        {
            return left.IsInteger()
                && right.GetNonNullableType() == typeof(int);
        }

        private static Type GetResultTypeOfShift(Type left, Type right)
        {
            if (!left.IsNullableType() && right.IsNullableType())
            {
                // lift the result type to Nullable<T>
                return left.LiftPrimitiveOrThrow();
            }
            return left;
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an bitwise left-shift operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LeftShift"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression LeftShift(Expression left, Expression right)
        {
            return LeftShift(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an bitwise left-shift operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.LeftShift"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression LeftShift(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (IsSimpleShift(left.Type, right.Type))
                {
                    Type resultType = GetResultTypeOfShift(left.Type, right.Type);
                    return new BinaryExpression(ExpressionType.LeftShift, left, right, resultType, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.LeftShift, "op_LeftShift", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.LeftShift, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an bitwise right-shift operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.RightShift"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression RightShift(Expression left, Expression right)
        {
            return RightShift(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an bitwise right-shift operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.RightShift"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression RightShift(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (IsSimpleShift(left.Type, right.Type))
                {
                    Type resultType = GetResultTypeOfShift(left.Type, right.Type);
                    return new BinaryExpression(ExpressionType.RightShift, left, right, resultType, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.RightShift, "op_RightShift", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.RightShift, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an bitwise AND operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.And"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression And(Expression left, Expression right)
        {
            return And(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an bitwise AND operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.And"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression And(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsIntegerOrBool())
                {
                    return new BinaryExpression(ExpressionType.And, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.And, "op_BitwiseAnd", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.And, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an bitwise OR operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Or"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Or(Expression left, Expression right)
        {
            return Or(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an bitwise OR operation.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Or"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Or(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsIntegerOrBool())
                {
                    return new BinaryExpression(ExpressionType.Or, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Or, "op_BitwiseOr", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Or, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise or logical XOR operation, using op_ExclusiveOr for user-defined types.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.ExclusiveOr"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression ExclusiveOr(Expression left, Expression right)
        {
            return ExclusiveOr(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents a bitwise or logical XOR operation, using op_ExclusiveOr for user-defined types.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.ExclusiveOr"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression ExclusiveOr(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsIntegerOrBool())
                {
                    return new BinaryExpression(ExpressionType.ExclusiveOr, left, right, left.Type, method: null);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.ExclusiveOr, "op_ExclusiveOr", left, right, liftToNull: true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.ExclusiveOr, left, right, method, liftToNull: true);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents raising a number to a power.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Power"/>
        /// and the <see cref="BinaryExpression.Left"/> and <see cref="BinaryExpression.Right"/> properties set to the specified values.</returns>
        public static BinaryExpression Power(Expression left, Expression right)
        {
            return Power(left, right, method: null);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents raising a number to a power.
        /// </summary>
        /// <param name="left">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Left"/> property equal to.</param>
        /// <param name="right">An <see cref="Expression"/> to set the <see cref="BinaryExpression.Right"/> property equal to.</param>
        /// <param name="method">A <see cref="MethodInfo"/> to set the <see cref="BinaryExpression.Method"/> property equal to.</param>
        /// <returns>A <see cref="BinaryExpression"/> that has the <see cref="NodeType"/> property equal to <see cref="ExpressionType.Power"/>
        /// and the <see cref="BinaryExpression.Left"/>, <see cref="BinaryExpression.Right"/>, and <see cref="BinaryExpression.Method"/> properties set to the specified values.
        /// </returns>
        public static BinaryExpression Power(Expression left, Expression right, MethodInfo? method)
        {
            ExpressionUtils.RequiresCanRead(left, nameof(left));
            ExpressionUtils.RequiresCanRead(right, nameof(right));
            if (method == null)
            {
                if (left.Type == right.Type && left.Type.IsArithmetic())
                {
                    method = Math_Pow_Double_Double;
                    Debug.Assert(method != null);
                }
                else
                {
                    // VB uses op_Exponent, F# uses op_Exponentiation. This inconsistency is unfortunate, but we can
                    // test for either.
                    string name = "op_Exponent";
                    BinaryExpression? b = GetUserDefinedBinaryOperator(ExpressionType.Power, name, left, right, liftToNull: true);
                    if (b == null)
                    {
                        name = "op_Exponentiation";
                        b = GetUserDefinedBinaryOperator(ExpressionType.Power, name, left, right, liftToNull: true);
                        if (b == null)
                        {
                            throw Error.BinaryOperatorNotDefined(ExpressionType.Power, left.Type, right.Type);
                        }
                    }

                    ParameterInfo[] pis = b.Method!.GetParameters();
                    ValidateParamsWithOperandsOrThrow(pis[0].ParameterType, left.Type, ExpressionType.Power, name);
                    ValidateParamsWithOperandsOrThrow(pis[1].ParameterType, right.Type, ExpressionType.Power, name);
                    return b;
                }
            }

            return GetMethodBasedBinaryOperator(ExpressionType.Power, left, right, method, liftToNull: true);
        }

        #endregion

        public static BinaryExpression ArrayIndex(Expression array, Expression index)
        {
            ExpressionUtils.RequiresCanRead(array, nameof(array));
            ExpressionUtils.RequiresCanRead(index, nameof(index));
            if (index.Type != typeof(int))
            {
                throw Error.ArgumentMustBeArrayIndexType(nameof(index));
            }

            Type arrayType = array.Type;
            if (!arrayType.IsArray)
            {
                throw Error.ArgumentMustBeArray(nameof(array));
            }
            if (arrayType.GetArrayRank() != 1)
            {
                throw Error.IncorrectNumberOfIndexes();
            }

            return new BinaryExpression(ExpressionType.ArrayIndex, array, index, arrayType.GetElementType()!, method: null);
        }

        private static void ValidateMethodInfo(MethodInfo method, string paramName)
        {
            if (method.ContainsGenericParameters)
                throw method.IsGenericMethodDefinition ? Error.MethodIsGeneric(method, paramName) : Error.MethodContainsGenericParameters(method, paramName);
        }

        internal static void ValidateOperatorMethod(
            MethodInfo method,
            Expression[] operands)
        {
            if (!method.IsStatic || method.ContainsGenericParameters)
            {
                throw new ArgumentException(
                    "An operator method must be a closed static method.",
                    nameof(method));
            }
            ExpressionValidation.ValidateArguments(
                method,
                operands,
                nameof(method));
            if (method.ReturnType == typeof(void))
            {
                throw new ArgumentException(
                    "An operator method must return a value.",
                    nameof(method));
            }
        }

        internal static bool IsNumeric(Type type)
        {
            if (type.IsEnum)
            {
                return false;
            }
            return Type.GetTypeCode(type) is
                TypeCode.SByte or TypeCode.Byte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
        }
    }
}
