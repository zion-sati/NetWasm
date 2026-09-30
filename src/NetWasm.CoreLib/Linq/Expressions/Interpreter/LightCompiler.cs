// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class LightCompiler
    {
        private readonly InstructionList _instructions = new();
        private readonly LocalVariables _locals = new();

        private LightCompiler()
        {
        }

        internal static Runtime.CompilerServices.ObjectArrayDelegateTarget Compile(
            LambdaExpression lambda)
        {
            ArgumentNullException.ThrowIfNull(lambda);
            if (lambda.Parameters.Count != 1)
            {
                throw Unsupported();
            }

            var parameter = lambda.Parameters[0];
            if (parameter.IsByRef || !IsSupportedValue(parameter.Type))
            {
                throw Unsupported();
            }
            if (lambda.ReturnType == typeof(void) ||
                !IsSupportedValue(lambda.ReturnType))
            {
                throw Unsupported();
            }

            var compiler = new LightCompiler();
            var parameterLocal = compiler._locals.Define(parameter);
            if (parameterLocal.Index != 0)
            {
                throw new InvalidOperationException(
                    "The interpreted lambda parameter has an invalid local index.");
            }

            compiler.Compile(lambda.Body, asVoid: false);
            var instructions = compiler._instructions.ToArray(
                expectedStackDepth: 1);
            return new LightLambda(new Interpreter(
                instructions,
                compiler._locals.LocalCount));
        }

        private void Compile(Expression expression, bool asVoid)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Parameter:
                    CompileParameter((ParameterExpression)expression);
                    break;
                case ExpressionType.Constant:
                    CompileConstant((ConstantExpression)expression);
                    break;
                case ExpressionType.Default:
                    CompileDefault((DefaultExpression)expression);
                    break;
                case ExpressionType.MemberAccess:
                    CompileMember((MemberExpression)expression);
                    break;
                case ExpressionType.Call:
                    CompileMethodCall((MethodCallExpression)expression);
                    break;
                case ExpressionType.Add:
                    CompileAdd((BinaryExpression)expression);
                    break;
                case ExpressionType.GreaterThan:
                case ExpressionType.LessThan:
                    CompileComparison((BinaryExpression)expression);
                    break;
                case ExpressionType.AndAlso:
                    CompileAndAlso((BinaryExpression)expression);
                    break;
                case ExpressionType.Negate:
                    CompileNegate((UnaryExpression)expression);
                    break;
                case ExpressionType.Convert:
                    CompileConvert((UnaryExpression)expression);
                    break;
                case ExpressionType.Assign:
                    CompileAssign((BinaryExpression)expression, asVoid);
                    return;
                case ExpressionType.Block:
                    CompileBlock((BlockExpression)expression, asVoid);
                    return;
                case ExpressionType.Conditional:
                    CompileConditional((ConditionalExpression)expression, asVoid);
                    return;
                default:
                    throw Unsupported();
            }

            if (asVoid && expression.Type != typeof(void))
            {
                _instructions.EmitPop();
            }
        }

        private void CompileParameter(ParameterExpression expression)
        {
            ValidateValue(expression.Type);
            _instructions.EmitLoadLocal(_locals.Resolve(expression));
        }

        private void CompileConstant(ConstantExpression expression)
        {
            ValidateValue(expression.Type);
            _instructions.EmitLoad(expression.Value);
        }

        private void CompileDefault(DefaultExpression expression)
        {
            if (expression.Type == typeof(void))
            {
                return;
            }
            ValidateValue(expression.Type);
            _instructions.EmitLoad(GetDefaultValue(expression.Type));
        }

        private void CompileMember(MemberExpression expression)
        {
            if (expression.Expression is null ||
                expression.Expression.Type.IsValueType)
            {
                throw Unsupported();
            }
            ValidateValue(expression.Type);
            switch (expression.Member)
            {
                case FieldInfo field when !field.IsStatic:
                    Compile(expression.Expression, asVoid: false);
                    _instructions.EmitLoadField(field);
                    return;
                case PropertyInfo property:
                    var getter = property.GetGetMethod(nonPublic: true);
                    if (getter is not null && !getter.IsStatic &&
                        getter.GetParameters().Length == 0 &&
                        getter.IsExpressionExecutable)
                    {
                        Compile(expression.Expression, asVoid: false);
                        _instructions.EmitCall(getter, argumentCount: 0);
                        return;
                    }
                    break;
            }
            throw Unsupported();
        }

        private void CompileMethodCall(MethodCallExpression expression)
        {
            var method = expression.Method;
            var parameters = method.GetParameters();
            if (!method.IsExpressionExecutable ||
                method.ReturnType == typeof(void) ||
                !IsSupportedValue(method.ReturnType) ||
                parameters.Length != expression.Arguments.Count)
            {
                throw Unsupported();
            }
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameterType = parameters[index].ParameterType;
                if (parameterType.IsByRef || !IsSupportedValue(parameterType))
                {
                    throw Unsupported();
                }
            }

            if (method.IsStatic)
            {
                if (expression.Object is not null)
                {
                    throw Unsupported();
                }
            }
            else if (expression.Object is null ||
                expression.Object.Type.IsValueType)
            {
                throw Unsupported();
            }

            if (expression.Object is not null)
            {
                Compile(expression.Object, asVoid: false);
            }
            foreach (var argument in expression.Arguments)
            {
                Compile(argument, asVoid: false);
            }
            _instructions.EmitCall(method, parameters.Length);
        }

        private void CompileAdd(BinaryExpression expression)
        {
            if (expression.Method is not null ||
                !IsArithmetic(expression.Type))
            {
                throw Unsupported();
            }
            Compile(expression.Left, asVoid: false);
            Compile(expression.Right, asVoid: false);
            _instructions.EmitAdd(expression.Type);
        }

        private void CompileComparison(BinaryExpression expression)
        {
            if (expression.Method is not null ||
                expression.Left.Type != expression.Right.Type ||
                !IsArithmetic(expression.Left.Type))
            {
                throw Unsupported();
            }
            Compile(expression.Left, asVoid: false);
            Compile(expression.Right, asVoid: false);
            if (expression.NodeType == ExpressionType.LessThan)
            {
                _instructions.EmitLessThan(expression.Left.Type);
            }
            else
            {
                _instructions.EmitGreaterThan(expression.Left.Type);
            }
        }

        private void CompileAndAlso(BinaryExpression expression)
        {
            if (expression.Method is not null ||
                expression.Left.Type != typeof(bool) ||
                expression.Right.Type != typeof(bool))
            {
                throw Unsupported();
            }

            var falseLabel = _instructions.MakeLabel();
            var endLabel = _instructions.MakeLabel();
            Compile(expression.Left, asVoid: false);
            _instructions.EmitBranchFalse(falseLabel);
            Compile(expression.Right, asVoid: false);
            _instructions.EmitBranch(endLabel, preservesValue: true);
            _instructions.MarkLabel(falseLabel);
            _instructions.EmitLoad(false);
            _instructions.MarkLabel(endLabel);
        }

        private void CompileNegate(UnaryExpression expression)
        {
            if (expression.Method is not null ||
                !IsSignedArithmetic(expression.Type))
            {
                throw Unsupported();
            }
            Compile(expression.Operand, asVoid: false);
            _instructions.EmitNegate(expression.Type);
        }

        private void CompileConvert(UnaryExpression expression)
        {
            if (expression.Method is not null ||
                !IsPrimitiveNumeric(expression.Operand.Type) ||
                !IsPrimitiveNumeric(expression.Type))
            {
                throw Unsupported();
            }
            Compile(expression.Operand, asVoid: false);
            var from = Type.GetTypeCode(expression.Operand.Type);
            var to = Type.GetTypeCode(expression.Type);
            if (from != to)
            {
                _instructions.EmitNumericConvert(from, to);
            }
        }

        private void CompileAssign(BinaryExpression expression, bool asVoid)
        {
            if (expression.Left is not ParameterExpression target)
            {
                throw Unsupported();
            }
            var localIndex = _locals.Resolve(target);
            ValidateValue(target.Type);
            Compile(expression.Right, asVoid: false);
            if (asVoid)
            {
                _instructions.EmitStoreLocal(localIndex);
            }
            else
            {
                _instructions.EmitAssignLocal(localIndex);
            }
        }

        private void CompileBlock(BlockExpression expression, bool asVoid)
        {
            var definitions = new LocalDefinition[expression.Variables.Count];
            var definedCount = 0;
            try
            {
                for (var index = 0; index < expression.Variables.Count; index++)
                {
                    var variable = expression.Variables[index];
                    ValidateValue(variable.Type);
                    var definition = _locals.Define(variable);
                    definitions[index] = definition;
                    definedCount++;
                    _instructions.EmitInitializeLocal(
                        definition.Index,
                        GetDefaultValue(variable.Type));
                }

                for (var index = 0; index < expression.Expressions.Count; index++)
                {
                    Compile(
                        expression.Expressions[index],
                        asVoid || index != expression.Expressions.Count - 1);
                }
            }
            finally
            {
                for (var index = definedCount - 1; index >= 0; index--)
                {
                    _locals.Undefine(definitions[index]);
                }
            }
        }

        private void CompileConditional(
            ConditionalExpression expression,
            bool asVoid)
        {
            if (expression.Test.Type != typeof(bool))
            {
                throw Unsupported();
            }

            var falseLabel = _instructions.MakeLabel();
            var endLabel = _instructions.MakeLabel();
            Compile(expression.Test, asVoid: false);
            _instructions.EmitBranchFalse(falseLabel);
            Compile(expression.IfTrue, asVoid);
            var preservesValue = !asVoid && expression.Type != typeof(void);
            _instructions.EmitBranch(endLabel, preservesValue);
            _instructions.MarkLabel(falseLabel);
            Compile(expression.IfFalse, asVoid);
            _instructions.MarkLabel(endLabel);
        }

        private static void ValidateValue(Type type)
        {
            if (!IsSupportedValue(type))
            {
                throw Unsupported();
            }
        }

        private static bool IsSupportedValue(Type type)
        {
            if (!type.IsValueType)
            {
                return !type.IsPointer;
            }
            if (type.IsEnum)
            {
                return false;
            }
            return Type.GetTypeCode(type) is
                TypeCode.Boolean or TypeCode.Char or
                TypeCode.SByte or TypeCode.Byte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double;
        }

        private static bool IsPrimitiveNumeric(Type type) =>
            !type.IsEnum && Type.GetTypeCode(type) is
                TypeCode.SByte or TypeCode.Byte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double;

        private static bool IsArithmetic(Type type) =>
            !type.IsEnum && Type.GetTypeCode(type) is
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double;

        private static bool IsSignedArithmetic(Type type) =>
            !type.IsEnum && Type.GetTypeCode(type) is
                TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
                TypeCode.Single or TypeCode.Double;

        private static object? GetDefaultValue(Type type)
        {
            if (!type.IsValueType)
            {
                return null;
            }
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Boolean => false,
                TypeCode.Char => '\0',
                TypeCode.SByte => (sbyte)0,
                TypeCode.Byte => (byte)0,
                TypeCode.Int16 => (short)0,
                TypeCode.UInt16 => (ushort)0,
                TypeCode.Int32 => 0,
                TypeCode.UInt32 => 0U,
                TypeCode.Int64 => 0L,
                TypeCode.UInt64 => 0UL,
                TypeCode.Single => 0F,
                TypeCode.Double => 0D,
                _ => throw Unsupported(),
            };
        }

        private static NotSupportedException Unsupported() =>
            new("This expression shape is not executable in the NetWasm profile.");
    }
}
