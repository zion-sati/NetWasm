// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: a lambda of up to 16 parameters, with a result or void, runs; the interpreter
// executes the operator nodes for the primitive types its value
// profile admits, with nullable lifting, and operator nodes that carry an expression-executable,
// non-lifted MethodInfo as calls, and type tests, reference, boxing and unboxing conversions on the runtime type
// table. Other valid trees (decimal, lifted user-defined operators,
// user-defined conditional logic, enum arithmetic, New with a constructor) are rejected while
// compiling, before any application code runs.

using System.Dynamic.Utils;
using System.Reflection;

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class LightCompiler
    {
        private readonly InstructionList _instructions = new();
        private readonly LocalVariables _locals = new();

        // Each label target's branch label, and the local a typed label's value passes through.
        private readonly System.Collections.Generic.Dictionary<LabelTarget, (BranchLabel Label, int? ValueLocal)> _labels = new();

        // Try regions, the catches a rethrow may refer to (innermost last), and how many finally or fault blocks
        // enclose the code being compiled, which a goto may not leave.
        private readonly System.Collections.Generic.List<TryRegion> _regions = new();
        private readonly System.Collections.Generic.List<int> _catchExceptions = new();
        private int _finallyDepth;
        private readonly System.Collections.Generic.Dictionary<LabelTarget, int> _labelFinallyDepths = new();
        private readonly System.Collections.Generic.List<(LabelTarget Target, int FinallyDepth)> _gotos = new();

        // The variables nested lambdas and quotes capture, whose locals hold a StrongBox from their definition, and
        // this lambda's own closure: the boxes of the enclosing frames' variables it captures, by position.
        private readonly System.Collections.Generic.HashSet<ParameterExpression> _captured;
        private readonly System.Collections.Generic.Dictionary<ParameterExpression, int> _closure = new();

        private LightCompiler(LambdaExpression lambda, System.Collections.Generic.List<ParameterExpression> closure)
        {
            _captured = ClosureVariableFinder.Captured(lambda);
            for (var index = 0; index < closure.Count; index++)
            {
                _closure.Add(closure[index], index);
            }
        }

        internal static Runtime.CompilerServices.ObjectArrayDelegateTarget Compile(
            LambdaExpression lambda)
        {
            ArgumentNullException.ThrowIfNull(lambda);
            return new LightLambda(CompileInterpreter(lambda, new()), lambda.Parameters.Count);
        }

        // A lambda's body, top-level or nested, with the closure it is given.
        private static Interpreter CompileInterpreter(
            LambdaExpression lambda,
            System.Collections.Generic.List<ParameterExpression> closure)
        {
            // The adapters stop at Func's and Action's 16 parameters.
            if (lambda.Parameters.Count > 16)
            {
                throw Unsupported();
            }
            var returnsValue = lambda.ReturnType != typeof(void);
            if (returnsValue && !ExpressionValidation.IsSupportedInterpreterValue(lambda.ReturnType))
            {
                throw Unsupported();
            }

            // The parameters are the first locals, in order, as the frame seeds them from the arguments; a captured
            // one's argument becomes the box its closures share.
            var compiler = new LightCompiler(lambda, closure);
            for (var index = 0; index < lambda.Parameters.Count; index++)
            {
                var parameter = lambda.Parameters[index];
                if (parameter.IsByRef ||
                    !ExpressionValidation.IsSupportedInterpreterValue(parameter.Type))
                {
                    throw Unsupported();
                }
                if (compiler._locals.Define(parameter).Index != index)
                {
                    throw new InvalidOperationException(
                        "The interpreted lambda parameter has an invalid local index.");
                }
                if (compiler._captured.Contains(parameter))
                {
                    compiler._instructions.Emit(new BoxLocalInstruction(index));
                }
            }

            compiler.Compile(lambda.Body, asVoid: !returnsValue);
            // A goto may not leave a finally or fault block's protection, which would skip it.
            foreach (var (target, finallyDepth) in compiler._gotos)
            {
                if (compiler._labelFinallyDepths.TryGetValue(target, out var labelDepth) && labelDepth != finallyDepth)
                {
                    throw Unsupported();
                }
            }
            var instructions = compiler._instructions.ToArray(
                expectedStackDepth: returnsValue ? 1 : 0);
            return new Interpreter(instructions, compiler._locals.LocalCount, returnsValue, [.. compiler._regions]);
        }

        private void Compile(Expression expression, bool asVoid)
        {
            if (expression.Type != typeof(void))
            {
                ValidateValue(expression.Type);
            }

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
                case ExpressionType.AddChecked:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.And:
                case ExpressionType.Or:
                case ExpressionType.ExclusiveOr:
                case ExpressionType.LeftShift:
                case ExpressionType.RightShift:
                case ExpressionType.Power:
                    CompileBinaryOperator((BinaryExpression)expression);
                    break;
                case ExpressionType.GreaterThan:
                case ExpressionType.LessThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThanOrEqual:
                    CompileComparison((BinaryExpression)expression);
                    break;
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                    CompileEqual((BinaryExpression)expression);
                    break;
                case ExpressionType.AndAlso:
                case ExpressionType.OrElse:
                    CompileLogical((BinaryExpression)expression);
                    break;
                case ExpressionType.Coalesce:
                    CompileCoalesce((BinaryExpression)expression);
                    break;
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.UnaryPlus:
                case ExpressionType.Not:
                case ExpressionType.OnesComplement:
                case ExpressionType.Increment:
                case ExpressionType.Decrement:
                case ExpressionType.IsTrue:
                case ExpressionType.IsFalse:
                    CompileUnaryOperator((UnaryExpression)expression);
                    break;
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    CompileConvert((UnaryExpression)expression);
                    break;
                case ExpressionType.New:
                    CompileNew((NewExpression)expression);
                    break;
                case ExpressionType.MemberInit:
                    CompileMemberInit((MemberInitExpression)expression);
                    break;
                case ExpressionType.ListInit:
                    CompileListInit((ListInitExpression)expression);
                    break;
                case ExpressionType.TypeIs:
                case ExpressionType.TypeEqual:
                    CompileTypeTest((TypeBinaryExpression)expression);
                    break;
                case ExpressionType.TypeAs:
                    CompileTypeAs((UnaryExpression)expression);
                    break;
                case ExpressionType.Unbox:
                    CompileUnbox((UnaryExpression)expression);
                    break;
                case ExpressionType.Throw:
                    CompileThrow((UnaryExpression)expression, asVoid);
                    return;
                case ExpressionType.Label:
                    CompileLabel((LabelExpression)expression, asVoid);
                    return;
                case ExpressionType.Goto:
                    CompileGoto((GotoExpression)expression, asVoid);
                    return;
                case ExpressionType.Loop:
                    CompileLoop((LoopExpression)expression, asVoid);
                    return;
                case ExpressionType.Switch:
                    CompileSwitch((SwitchExpression)expression, asVoid);
                    return;
                case ExpressionType.Try:
                    CompileTry((TryExpression)expression, asVoid);
                    return;
                case ExpressionType.ArrayLength:
                    Compile(((UnaryExpression)expression).Operand, asVoid: false);
                    _instructions.Emit(new ArrayLengthInstruction());
                    break;
                case ExpressionType.ArrayIndex:
                    CompileArrayIndex((BinaryExpression)expression);
                    break;
                case ExpressionType.Index:
                    CompileIndex((IndexExpression)expression);
                    break;
                case ExpressionType.NewArrayInit:
                case ExpressionType.NewArrayBounds:
                    CompileNewArray((NewArrayExpression)expression);
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
                case ExpressionType.Lambda:
                    CompileLambda((LambdaExpression)expression);
                    break;
                case ExpressionType.Quote:
                    CompileQuote((UnaryExpression)expression);
                    break;
                case ExpressionType.Invoke:
                    CompileInvoke((InvocationExpression)expression, asVoid);
                    return;
                default:
                    // Compound and increment/decrement assignments execute through their reduction; other node
                    // types are unsupported.
                    if (expression.CanReduce)
                    {
                        Compile(expression.ReduceAndCheck(), asVoid);
                        return;
                    }
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
            EmitLoadVariable(expression);
        }

        // A variable of this lambda, boxed when a nested lambda or quote captures it, or one its closure holds.
        private void EmitLoadVariable(ParameterExpression variable)
        {
            if (_locals.TryResolve(variable, out var index))
            {
                _instructions.Emit(_captured.Contains(variable)
                    ? new LoadBoxedLocalInstruction(index)
                    : new LoadLocalInstruction(index));
            }
            else if (_closure.TryGetValue(variable, out var closureIndex))
            {
                _instructions.Emit(new LoadClosureInstruction(closureIndex));
            }
            else
            {
                _locals.Resolve(variable);
            }
        }

        private void EmitStoreVariable(ParameterExpression variable, bool preservesValue)
        {
            if (_locals.TryResolve(variable, out var index))
            {
                if (_captured.Contains(variable))
                {
                    _instructions.Emit(new StoreBoxedLocalInstruction(index, preservesValue));
                }
                else if (preservesValue)
                {
                    _instructions.EmitAssignLocal(index);
                }
                else
                {
                    _instructions.EmitStoreLocal(index);
                }
            }
            else if (_closure.TryGetValue(variable, out var closureIndex))
            {
                _instructions.Emit(new StoreClosureInstruction(closureIndex, preservesValue));
            }
            else
            {
                _locals.Resolve(variable);
            }
        }

        // The box of a captured variable, for a nested lambda's closure or a quote.
        private QuotedVariable EmitLoadBox(ParameterExpression variable)
        {
            if (_locals.TryResolve(variable, out var index))
            {
                if (!_captured.Contains(variable))
                {
                    throw new InvalidOperationException("A captured interpreted variable is not boxed.");
                }
                _instructions.EmitLoadLocal(index);
                return new QuotedVariable(InClosure: false, index);
            }
            if (_closure.TryGetValue(variable, out var closureIndex))
            {
                _instructions.Emit(new LoadClosureBoxInstruction(closureIndex));
                return new QuotedVariable(InClosure: true, closureIndex);
            }
            _locals.Resolve(variable);
            throw new InvalidOperationException("A parameter expression is referenced outside its defining scope.");
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

        // A field, or a property's getter, of an object or a struct (on its box), or static. A nullable's members
        // are not compiled: its value is held as the underlying value's box, not as a Nullable<T>.
        private void CompileMember(MemberExpression expression)
        {
            ValidateValue(expression.Type);
            var receiver = expression.Expression;
            if (receiver is not null && receiver.Type.IsNullable)
            {
                throw Unsupported();
            }
            switch (expression.Member)
            {
                case FieldInfo field when !field.IsLiteral && field.IsStatic == (receiver is null):
                    if (receiver is not null)
                    {
                        Compile(receiver, asVoid: false);
                    }
                    _instructions.EmitLoadField(field);
                    return;
                case PropertyInfo property:
                    var getter = property.GetGetMethod(nonPublic: true);
                    if (getter is not null && getter.IsStatic == (receiver is null) &&
                        getter.GetParameters().Length == 0 &&
                        getter.IsExpressionExecutable)
                    {
                        if (receiver is not null)
                        {
                            Compile(receiver, asVoid: false);
                        }
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
            var returnsVoid = method.ReturnType == typeof(void);
            if (!method.IsExpressionExecutable ||
                !returnsVoid && !ExpressionValidation.IsSupportedInterpreterValue(method.ReturnType) ||
                parameters.Length != expression.Arguments.Count)
            {
                throw Unsupported();
            }
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameterType = parameters[index].ParameterType;
                if (!IsSupportedMethodParameter(parameterType))
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
                expression.Object.Type.IsNullable)
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
            _instructions.EmitCall(method, parameters.Length, producesValue: !returnsVoid);
        }

        private void CompileBinaryOperator(BinaryExpression expression)
        {
            if (TryCompileDecimalBinary(expression))
            {
                return;
            }
            if (expression.Method is not null)
            {
                CompileOperatorCall(expression, expression.Method, expression.IsLifted, expression.Left, expression.Right);
                return;
            }
            Type operandType = expression.Left.Type.GetNonNullableType();
            bool admitted = expression.NodeType switch
            {
                ExpressionType.And or ExpressionType.Or or ExpressionType.ExclusiveOr =>
                    expression.Left.Type == expression.Right.Type && expression.Left.Type.IsIntegerOrBool(),
                ExpressionType.LeftShift or ExpressionType.RightShift =>
                    expression.Left.Type.IsInteger() && expression.Right.Type.GetNonNullableType() == typeof(int),
                // Power always carries Math.Pow or a user-defined method.
                ExpressionType.Power => false,
                _ => expression.Left.Type == expression.Right.Type && expression.Left.Type.IsArithmetic(),
            };
            if (!admitted)
            {
                throw Unsupported();
            }
            Compile(expression.Left, asVoid: false);
            Compile(expression.Right, asVoid: false);
            _instructions.EmitBinaryOperator(expression.NodeType, operandType);
        }

        private void CompileComparison(BinaryExpression expression)
        {
            if (TryCompileDecimalBinary(expression))
            {
                return;
            }
            if (expression.Method is not null)
            {
                CompileOperatorCall(expression, expression.Method, expression.IsLifted, expression.Left, expression.Right);
                return;
            }
            if (expression.Left.Type != expression.Right.Type ||
                !expression.Left.Type.IsNumeric())
            {
                throw Unsupported();
            }
            Compile(expression.Left, asVoid: false);
            Compile(expression.Right, asVoid: false);
            _instructions.EmitComparison(
                expression.Left.Type.GetNonNullableType(),
                expression.NodeType switch
                {
                    ExpressionType.LessThan => NumericComparison.LessThan,
                    ExpressionType.GreaterThan => NumericComparison.GreaterThan,
                    ExpressionType.LessThanOrEqual => NumericComparison.LessThanOrEqual,
                    _ => NumericComparison.GreaterThanOrEqual,
                },
                expression.IsLiftedToNull);
        }

        private void CompileEqual(BinaryExpression expression)
        {
            if (TryCompileDecimalBinary(expression))
            {
                return;
            }
            if (expression.Method is not null)
            {
                CompileOperatorCall(expression, expression.Method, expression.IsLifted, expression.Left, expression.Right);
                return;
            }
            if (expression.Left.Type != expression.Right.Type ||
                !ExpressionValidation.IsSupportedInterpreterEqualityOperand(
                    expression.Left.Type))
            {
                throw Unsupported();
            }
            Compile(expression.Left, asVoid: false);
            Compile(expression.Right, asVoid: false);
            _instructions.EmitEqual(
                expression.Left.Type,
                expression.IsLiftedToNull,
                expression.NodeType == ExpressionType.NotEqual,
                expression.ComparesStringValues);
        }

        private void CompileLogical(BinaryExpression expression)
        {
            if (expression.Method is not null ||
                expression.Left.Type != typeof(bool) ||
                expression.Right.Type != typeof(bool))
            {
                throw Unsupported();
            }

            // AndAlso skips the right operand when the left is false, OrElse when it is true.
            var shortCircuitLabel = _instructions.MakeLabel();
            var endLabel = _instructions.MakeLabel();
            Compile(expression.Left, asVoid: false);
            if (expression.NodeType == ExpressionType.OrElse)
            {
                _instructions.EmitUnaryOperator(ExpressionType.Not, typeof(bool));
            }
            _instructions.EmitBranchFalse(shortCircuitLabel);
            Compile(expression.Right, asVoid: false);
            _instructions.EmitBranch(endLabel, preservesValue: true);
            _instructions.MarkLabel(shortCircuitLabel);
            _instructions.EmitLoad(expression.NodeType == ExpressionType.OrElse);
            _instructions.MarkLabel(endLabel);
        }

        private void CompileCoalesce(BinaryExpression expression)
        {
            // The interpreter keeps a nullable as null or its boxed value, so only coalesces whose
            // operands need no numeric or user-defined conversion to the result type execute.
            if (expression.Conversion is not null ||
                !IsRepresentationPreserving(expression.Left.Type, expression.Type) ||
                !IsRepresentationPreserving(expression.Right.Type, expression.Type))
            {
                throw Unsupported();
            }
            var endLabel = _instructions.MakeLabel();
            Compile(expression.Left, asVoid: false);
            _instructions.EmitCoalescingBranch(endLabel);
            Compile(expression.Right, asVoid: false);
            _instructions.MarkLabel(endLabel);
        }

        private static bool IsRepresentationPreserving(Type source, Type target) =>
            TypeUtils.AreEquivalent(source.GetNonNullableType(), target.GetNonNullableType()) ||
            !source.IsValueType && TypeUtils.AreReferenceAssignable(target, source);

        private void CompileUnaryOperator(UnaryExpression expression)
        {
            if (DecimalOperators.Classify(expression.Method) is { } decimalOperator and not DecimalOperatorKind.Convert)
            {
                Compile(expression.Operand, asVoid: false);
                _instructions.Emit(new DecimalUnaryInstruction(decimalOperator));
                return;
            }
            if (expression.Method is not null)
            {
                CompileOperatorCall(expression, expression.Method, expression.IsLifted, expression.Operand);
                return;
            }
            Type operand = expression.Operand.Type;
            bool admitted = expression.NodeType switch
            {
                ExpressionType.Negate or ExpressionType.NegateChecked =>
                    operand.IsArithmetic() && !operand.IsUnsignedInt(),
                ExpressionType.UnaryPlus or ExpressionType.Increment or ExpressionType.Decrement =>
                    operand.IsArithmetic(),
                ExpressionType.Not => operand.IsIntegerOrBool(),
                ExpressionType.OnesComplement => operand.IsInteger(),
                _ => operand.IsBool(),
            };
            if (!admitted)
            {
                throw Unsupported();
            }
            Compile(expression.Operand, asVoid: false);
            if (expression.NodeType != ExpressionType.UnaryPlus)
            {
                _instructions.EmitUnaryOperator(expression.NodeType, operand.GetNonNullableType());
            }
        }

        // A user-defined operator, or one supplied as a MethodInfo, runs as a call when it is not
        // lifted and the member execution plan admits it.
        // A lifted operator takes its nullable operands' values; when one is null, the node's lifted result stands
        // in for the call (LiftedCallInstruction).
        private void CompileOperatorCall(Expression node, MethodInfo method, bool isLifted, params Expression[] operands)
        {
            var parameters = method.GetParameters();
            if (!method.IsStatic ||
                !method.IsExpressionExecutable ||
                parameters.Length != operands.Length ||
                !(isLifted || TypeUtils.AreReferenceAssignable(node.Type, method.ReturnType)) ||
                !ExpressionValidation.IsSupportedInterpreterValue(method.ReturnType))
            {
                throw Unsupported();
            }
            for (var index = 0; index < parameters.Length; index++)
            {
                var operandType = isLifted ? operands[index].Type.GetNonNullableType() : operands[index].Type;
                if (!IsSupportedMethodParameter(parameters[index].ParameterType) ||
                    !TypeUtils.AreReferenceAssignable(parameters[index].ParameterType, operandType))
                {
                    throw Unsupported();
                }
            }
            foreach (var operand in operands)
            {
                Compile(operand, asVoid: false);
            }
            var call = new CallInstruction(method, operands.Length, producesValue: true);
            _instructions.Emit(isLifted ? new LiftedCallInstruction(call, LiftedCallInstruction.NullResultOf(node)) : call);
        }

        // New runs its constructor on a new object, or on a struct's box; New(Type) of a struct is its default.
        private void CompileNew(NewExpression expression)
        {
            ValidateValue(expression.Type);
            if (expression.Constructor is not { } constructor)
            {
                if (expression.Arguments.Count != 0 || !expression.Type.IsValueType)
                {
                    throw Unsupported();
                }
                _instructions.Emit(new NewDefaultValueInstruction(expression.Type));
                return;
            }
            var parameters = constructor.GetParameters();
            if (!constructor.IsExpressionExecutable || parameters.Length != expression.Arguments.Count)
            {
                throw Unsupported();
            }
            foreach (var parameter in parameters)
            {
                if (!IsSupportedMethodParameter(parameter.ParameterType))
                {
                    throw Unsupported();
                }
            }
            foreach (var argument in expression.Arguments)
            {
                Compile(argument, asVoid: false);
            }
            _instructions.EmitCall(constructor, parameters.Length);
        }

        // TypeIs and TypeEqual test the value's runtime type against the type table.
        private void CompileTypeTest(TypeBinaryExpression expression)
        {
            Compile(expression.Expression, asVoid: false);
            _instructions.Emit(expression.NodeType == ExpressionType.TypeIs
                ? new TypeIsInstruction(expression.TypeOperand)
                : new TypeEqualInstruction(expression.TypeOperand));
        }

        private void CompileTypeAs(UnaryExpression expression)
        {
            Compile(expression.Operand, asVoid: false);
            _instructions.Emit(new TypeAsInstruction(expression.Type));
        }

        // Unbox reads a boxed value as its value type; the interpreter already holds it boxed, so only the type is
        // checked.
        private void CompileUnbox(UnaryExpression expression)
        {
            Compile(expression.Operand, asVoid: false);
            _instructions.Emit(new CastInstruction(expression.Type));
        }

        // Throw: the exception, then nothing, though a throw typed as a value keeps the stack's shape. A rethrow
        // needs a catch, which the interpreter does not compile.
        private void CompileThrow(UnaryExpression expression, bool asVoid)
        {
            if (expression.Operand is null)
            {
                // A rethrow throws the innermost enclosing catch's exception again.
                if (_catchExceptions.Count == 0)
                {
                    throw Unsupported();
                }
                _instructions.Emit(new RethrowInstruction(_catchExceptions[^1]));
            }
            else
            {
                Compile(expression.Operand, asVoid: false);
                _instructions.Emit(new ThrowInstruction());
            }
            if (!asVoid && expression.Type != typeof(void))
            {
                _instructions.EmitLoad(null);
            }
        }

        private (BranchLabel Label, int? ValueLocal) ResolveLabel(LabelTarget target)
        {
            if (!_labels.TryGetValue(target, out var label))
            {
                int? valueLocal = null;
                if (target.Type != typeof(void))
                {
                    ValidateValue(target.Type);
                    valueLocal = _locals.Define(Expression.Variable(target.Type)).Index;
                }
                label = (_instructions.MakeLabel(), valueLocal);
                _labels.Add(target, label);
            }
            return label;
        }

        // A label: a typed one first stores its default value, which a goto carrying a value replaces, then its
        // value is read after the mark.
        private void CompileLabel(LabelExpression expression, bool asVoid)
        {
            var (label, valueLocal) = ResolveLabel(expression.Target);
            if (valueLocal is { } local)
            {
                if (expression.DefaultValue is not null)
                {
                    Compile(expression.DefaultValue, asVoid: false);
                }
                else
                {
                    _instructions.EmitLoad(GetDefaultValue(expression.Target.Type));
                }
                _instructions.EmitStoreLocal(local);
            }
            else if (expression.DefaultValue is not null)
            {
                Compile(expression.DefaultValue, asVoid: true);
            }
            _labelFinallyDepths[expression.Target] = _finallyDepth;
            _instructions.MarkLabel(label);
            if (valueLocal is { } value && !asVoid)
            {
                _instructions.EmitLoadLocal(value);
            }
        }

        // Goto, Break, Continue and Return branch to the label, carrying a typed label's value in its local. The code
        // after a goto is unreachable; a goto typed as a value keeps the stack's shape.
        private void CompileGoto(GotoExpression expression, bool asVoid)
        {
            var (label, valueLocal) = ResolveLabel(expression.Target);
            _gotos.Add((expression.Target, _finallyDepth));
            if (expression.Value is not null)
            {
                Compile(expression.Value, asVoid: valueLocal is null);
                if (valueLocal is { } local)
                {
                    _instructions.EmitStoreLocal(local);
                }
            }
            _instructions.EmitBranch(label, preservesValue: false);
            if (!asVoid && expression.Type != typeof(void))
            {
                _instructions.EmitLoad(null);
            }
        }

        // A loop runs its body until a goto leaves it: Continue returns to the start, Break to after the loop, whose
        // value a typed break label carries.
        private void CompileLoop(LoopExpression expression, bool asVoid)
        {
            if (expression.ContinueLabel is { } continueLabel)
            {
                _labelFinallyDepths[continueLabel] = _finallyDepth;
            }
            if (expression.BreakLabel is { } breakLabel)
            {
                _labelFinallyDepths[breakLabel] = _finallyDepth;
            }
            var start = expression.ContinueLabel is { } continueTarget
                ? ResolveLabel(continueTarget).Label
                : _instructions.MakeLabel();
            _instructions.MarkLabel(start);
            Compile(expression.Body, asVoid: true);
            _instructions.EmitBranch(start, preservesValue: false);
            if (expression.BreakLabel is { } breakTarget)
            {
                var (label, valueLocal) = ResolveLabel(breakTarget);
                _instructions.MarkLabel(label);
                if (valueLocal is { } value && !asVoid)
                {
                    _instructions.EmitLoadLocal(value);
                }
            }
        }

        // A switch tests its value against each case's test values in order, through the comparison method or the
        // values' equality, and runs the first matching case's body or else the default body.
        private void CompileSwitch(SwitchExpression expression, bool asVoid)
        {
            var hasValue = !asVoid && expression.Type != typeof(void);
            var switchValue = Expression.Variable(expression.SwitchValue.Type);
            var valueDefinition = _locals.Define(switchValue);
            try
            {
                Compile(expression.SwitchValue, asVoid: false);
                _instructions.EmitStoreLocal(valueDefinition.Index);
                var caseLabels = new BranchLabel[expression.Cases.Count];
                for (var index = 0; index < expression.Cases.Count; index++)
                {
                    caseLabels[index] = _instructions.MakeLabel();
                    foreach (var test in expression.Cases[index].TestValues)
                    {
                        _instructions.EmitLoadLocal(valueDefinition.Index);
                        Compile(test, asVoid: false);
                        if (expression.Comparison is { } comparison)
                        {
                            if (!comparison.IsStatic || !comparison.IsExpressionExecutable)
                            {
                                throw Unsupported();
                            }
                            _instructions.EmitCall(comparison, argumentCount: 2);
                        }
                        else if (expression.SwitchValue.Type.GetNonNullableType() is var type &&
                            (type == typeof(float) || type == typeof(double)))
                        {
                            _instructions.EmitEqual(expression.SwitchValue.Type, liftedToNull: false, notEqual: false, comparesStringValues: false);
                        }
                        else
                        {
                            _instructions.Emit(new ValueEqualsInstruction());
                        }
                        _instructions.EmitUnaryOperator(ExpressionType.Not, typeof(bool));
                        _instructions.EmitBranchFalse(caseLabels[index]);
                    }
                }
                var defaultLabel = _instructions.MakeLabel();
                var endLabel = _instructions.MakeLabel();
                _instructions.EmitBranch(defaultLabel, preservesValue: false);
                for (var index = 0; index < expression.Cases.Count; index++)
                {
                    _instructions.MarkLabel(caseLabels[index]);
                    Compile(expression.Cases[index].Body, asVoid: !hasValue);
                    _instructions.EmitBranch(endLabel, preservesValue: hasValue);
                }
                _instructions.MarkLabel(defaultLabel);
                if (expression.DefaultBody is not null)
                {
                    Compile(expression.DefaultBody, asVoid: !hasValue);
                }
                _instructions.MarkLabel(endLabel);
            }
            finally
            {
                _locals.Undefine(valueDefinition);
            }
        }

        // A try: its body, then its catch handlers, each storing the exception it takes, then a finally block that
        // both paths run (an exceptional entry rethrows at its end) or a fault block that only an exception runs.
        // Catch filters are outside the profile.
        private void CompileTry(TryExpression expression, bool asVoid)
        {
            foreach (var handler in expression.Handlers)
            {
                if (handler.Filter is not null)
                {
                    throw Unsupported();
                }
            }
            var hasValue = !asVoid && expression.Type != typeof(void);
            LocalDefinition? result = hasValue ? _locals.Define(Expression.Variable(expression.Type)) : null;
            var protects = expression.Finally is not null || expression.Fault is not null;
            var region = new TryRegion
            {
                TryStart = _instructions.Count,
                StackDepth = _instructions.CurrentStackDepth,
            };
            var afterHandlers = _instructions.MakeLabel();
            var catches = new System.Collections.Generic.List<CatchHandler>();
            if (protects)
            {
                _finallyDepth++;
            }
            try
            {
                Compile(expression.Body, asVoid: !hasValue);
                if (result is { } bodyResult)
                {
                    _instructions.EmitStoreLocal(bodyResult.Index);
                }
                _instructions.EmitBranch(afterHandlers, preservesValue: false);
                region.TryEnd = _instructions.Count;
                foreach (var handler in expression.Handlers)
                {
                    var variable = handler.Variable ?? Expression.Variable(typeof(Exception));
                    // A captured catch variable is a box copied from the exception the handler takes, whose own
                    // local rethrow keeps reading.
                    var captured = _captured.Contains(variable);
                    var exceptionLocal = _locals.Define(captured ? Expression.Variable(typeof(Exception)) : variable);
                    LocalDefinition? boxedLocal = captured ? _locals.Define(variable) : null;
                    catches.Add(new CatchHandler(handler.Test, _instructions.Count, exceptionLocal.Index));
                    _catchExceptions.Add(exceptionLocal.Index);
                    try
                    {
                        if (boxedLocal is { } boxed)
                        {
                            _instructions.Emit(new CopyToBoxedLocalInstruction(exceptionLocal.Index, boxed.Index));
                        }
                        Compile(handler.Body, asVoid: !hasValue);
                        if (result is { } handlerResult)
                        {
                            _instructions.EmitStoreLocal(handlerResult.Index);
                        }
                        _instructions.EmitBranch(afterHandlers, preservesValue: false);
                    }
                    finally
                    {
                        _catchExceptions.RemoveAt(_catchExceptions.Count - 1);
                        if (boxedLocal is { } boxed)
                        {
                            _locals.Undefine(boxed);
                        }
                        _locals.Undefine(exceptionLocal);
                    }
                }
                region.HandlersEnd = _instructions.Count;
            }
            finally
            {
                if (protects)
                {
                    _finallyDepth--;
                }
            }
            region.Catches = [.. catches];
            _instructions.MarkLabel(afterHandlers);
            if (protects)
            {
                var pending = _locals.Define(Expression.Variable(typeof(Exception)));
                region.PendingLocal = pending.Index;
                var afterFault = _instructions.MakeLabel();
                if (expression.Finally is not null)
                {
                    // The normal path enters with no pending exception.
                    _instructions.EmitLoad(null);
                    _instructions.EmitStoreLocal(pending.Index);
                }
                else
                {
                    _instructions.EmitBranch(afterFault, preservesValue: false);
                }
                region.FinallyStart = _instructions.Count;
                Compile(expression.Finally ?? expression.Fault!, asVoid: true);
                _instructions.Emit(new EndFinallyInstruction(pending.Index));
                region.FinallyEnd = _instructions.Count;
                _instructions.MarkLabel(afterFault);
            }
            _regions.Add(region);
            if (result is { } value)
            {
                _instructions.EmitLoadLocal(value.Index);
                _locals.Undefine(value);
            }
        }

        // ArrayIndex reads one element of a one-dimensional array.
        private void CompileArrayIndex(BinaryExpression expression)
        {
            Compile(expression.Left, asVoid: false);
            Compile(expression.Right, asVoid: false);
            _instructions.Emit(new ArrayGetInstruction(indexCount: 1));
        }

        // An index without an indexer reads an array element, one index per rank; an indexer runs its getter as a
        // call.
        private void CompileIndex(IndexExpression expression)
        {
            if (expression.Indexer is { } indexer)
            {
                var getter = indexer.GetGetMethod(nonPublic: true) ?? throw Unsupported();
                CompileMethodCall(Expression.Call(expression.Object, getter, expression.Arguments));
                return;
            }
            Compile(expression.Object!, asVoid: false);
            foreach (var argument in expression.Arguments)
            {
                Compile(argument, asVoid: false);
            }
            _instructions.Emit(new ArrayGetInstruction(expression.Arguments.Count));
        }

        // NewArrayBounds creates one-dimensional arrays only (see ArrayInstructions).
        private void CompileNewArray(NewArrayExpression expression)
        {
            if (expression.NodeType == ExpressionType.NewArrayBounds && expression.Expressions.Count != 1)
            {
                throw Unsupported();
            }
            var elementType = expression.Type.GetElementType()!;
            foreach (var item in expression.Expressions)
            {
                Compile(item, asVoid: false);
            }
            _instructions.Emit(expression.NodeType == ExpressionType.NewArrayInit
                ? new NewArrayInitInstruction(elementType, expression.Expressions.Count)
                : new NewArrayBoundsInstruction(elementType));
        }

        // Decimal's binary operators, which the interpreter runs itself, lifted as the node is.
        private bool TryCompileDecimalBinary(BinaryExpression expression)
        {
            if (DecimalOperators.Classify(expression.Method) is not { } decimalOperator ||
                decimalOperator is DecimalOperatorKind.Negate or DecimalOperatorKind.UnaryPlus or
                    DecimalOperatorKind.Increment or DecimalOperatorKind.Decrement or DecimalOperatorKind.Convert)
            {
                return false;
            }
            Compile(expression.Left, asVoid: false);
            Compile(expression.Right, asVoid: false);
            _instructions.Emit(new DecimalBinaryInstruction(decimalOperator, expression.IsLiftedToNull));
            return true;
        }

        private void CompileConvert(UnaryExpression expression)
        {
            if (DecimalOperators.Classify(expression.Method) == DecimalOperatorKind.Convert)
            {
                Compile(expression.Operand, asVoid: false);
                _instructions.Emit(new DecimalConvertInstruction(
                    Type.GetTypeCode(expression.Operand.Type.GetNonNullableType()),
                    Type.GetTypeCode(expression.Type.GetNonNullableType()),
                    expression.Type.IsNullable));
                return;
            }
            if (expression.Method is not null)
            {
                CompileOperatorCall(expression, expression.Method, expression.IsLifted, expression.Operand);
                return;
            }
            Type source = expression.Operand.Type;
            Type target = expression.Type;
            // Reference, boxing and unboxing conversions keep the value's representation: widening ones need no
            // check, and narrowing ones (a downcast, an interface cast, unboxing) check the runtime type.
            if (!source.IsValueType || !target.IsValueType)
            {
                Compile(expression.Operand, asVoid: false);
                if (!target.IsAssignableFrom(source))
                {
                    _instructions.Emit(new CastInstruction(target));
                }
                return;
            }
            Type fromType = Nullable.GetUnderlyingType(source) ?? source;
            Type toType = Nullable.GetUnderlyingType(target) ?? target;
            if (fromType != toType &&
                (!IsPrimitiveNumeric(fromType) || !IsPrimitiveNumeric(toType)))
            {
                throw Unsupported();
            }
            Compile(expression.Operand, asVoid: false);
            // The interpreter stores a nullable as null or its boxed underlying value.
            // Wrapping is therefore an identity operation; unwrapping must still check null.
            if (fromType == toType)
            {
                if (source.IsNullable && !target.IsNullable)
                {
                    _instructions.EmitNullableValue();
                }
                return;
            }
            _instructions.EmitNumericConvert(
                Type.GetTypeCode(fromType),
                Type.GetTypeCode(toType),
                target.IsNullable,
                expression.NodeType == ExpressionType.ConvertChecked);
        }

        private void CompileAssign(BinaryExpression expression, bool asVoid)
        {
            // An array element: the array, its indexes, then the value.
            if (expression.Left is IndexExpression { Indexer: null } element)
            {
                Compile(element.Object!, asVoid: false);
                foreach (var argument in element.Arguments)
                {
                    Compile(argument, asVoid: false);
                }
                Compile(expression.Right, asVoid: false);
                _instructions.Emit(new ArraySetInstruction(element.Arguments.Count, preservesValue: !asVoid));
                return;
            }
            if (expression.Left is MemberExpression member)
            {
                CompileMemberAssign(member, expression.Right, asVoid);
                return;
            }
            if (expression.Left is not ParameterExpression target)
            {
                throw Unsupported();
            }
            ValidateValue(target.Type);
            Compile(expression.Right, asVoid: false);
            EmitStoreVariable(target, preservesValue: !asVoid);
        }

        // A field, or a property through its setter, of an object or a struct (changed in its box), or static. The
        // receiver is evaluated before the value, as compiled code does.
        private void CompileMemberAssign(MemberExpression member, Expression value, bool asVoid)
        {
            var receiver = member.Expression;
            if (receiver is not null && receiver.Type.IsNullable)
            {
                throw Unsupported();
            }
            ValidateValue(member.Type);
            switch (member.Member)
            {
                case FieldInfo field when !field.IsLiteral && !field.IsInitOnly && !field.FieldType.IsNullable &&
                    field.IsStatic == (receiver is null):
                    if (receiver is not null)
                    {
                        Compile(receiver, asVoid: false);
                    }
                    Compile(value, asVoid: false);
                    _instructions.Emit(new StoreFieldInstruction(field, preservesValue: !asVoid));
                    return;
                case PropertyInfo property:
                    var setter = property.GetSetMethod(nonPublic: true);
                    if (setter is not null && setter.IsStatic == (receiver is null) &&
                        setter.GetParameters().Length == 1 &&
                        IsSupportedMethodParameter(setter.GetParameters()[0].ParameterType) &&
                        setter.IsExpressionExecutable)
                    {
                        if (receiver is not null)
                        {
                            Compile(receiver, asVoid: false);
                        }
                        Compile(value, asVoid: false);
                        _instructions.Emit(new StorePropertyInstruction(setter, preservesValue: !asVoid));
                        return;
                    }
                    break;
            }
            throw Unsupported();
        }

        // New, then each binding on the new object (a struct's in its box): an assignment, a nested binding on a
        // member that holds a reference, or a collection member's Add calls.
        private void CompileMemberInit(MemberInitExpression expression)
        {
            CompileNew(expression.NewExpression);
            CompileBindings(expression.Bindings);
        }

        private void CompileBindings(System.Collections.ObjectModel.ReadOnlyCollection<MemberBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                _instructions.Emit(DupInstruction.Instance);
                switch (binding)
                {
                    case MemberAssignment assignment:
                        CompileBindingAssign(assignment);
                        break;
                    case MemberMemberBinding nested when !MemberType(nested.Member).IsValueType:
                        CompileMemberRead(nested.Member);
                        CompileBindings(nested.Bindings);
                        _instructions.EmitPop();
                        break;
                    case MemberListBinding list when !MemberType(list.Member).IsValueType:
                        CompileMemberRead(list.Member);
                        CompileElementInits(list.Initializers);
                        _instructions.EmitPop();
                        break;
                    default:
                        throw Unsupported();
                }
            }
        }

        // The object is on the stack; the binding's member takes the value.
        private void CompileBindingAssign(MemberAssignment assignment)
        {
            switch (assignment.Member)
            {
                case FieldInfo field when !field.IsStatic && !field.IsLiteral && !field.IsInitOnly &&
                    !field.FieldType.IsNullable:
                    Compile(assignment.Expression, asVoid: false);
                    _instructions.Emit(new StoreFieldInstruction(field, preservesValue: false));
                    return;
                case PropertyInfo property when property.GetSetMethod(nonPublic: true) is
                { IsStatic: false, IsExpressionExecutable: true } setter &&
                    setter.GetParameters().Length == 1 &&
                    IsSupportedMethodParameter(setter.GetParameters()[0].ParameterType):
                    Compile(assignment.Expression, asVoid: false);
                    _instructions.Emit(new StorePropertyInstruction(setter, preservesValue: false));
                    return;
            }
            throw Unsupported();
        }

        // The object is on the stack; it is replaced by the member's value.
        private void CompileMemberRead(MemberInfo member)
        {
            switch (member)
            {
                case FieldInfo { IsStatic: false, IsLiteral: false } field:
                    _instructions.EmitLoadField(field);
                    return;
                case PropertyInfo property when property.GetGetMethod(nonPublic: true) is
                { IsStatic: false, IsExpressionExecutable: true } getter && getter.GetParameters().Length == 0:
                    _instructions.EmitCall(getter, argumentCount: 0);
                    return;
            }
            throw Unsupported();
        }

        private static Type MemberType(MemberInfo member) => member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => throw Unsupported(),
        };

        // New, then the collection's Add for each element.
        private void CompileListInit(ListInitExpression expression)
        {
            CompileNew(expression.NewExpression);
            CompileElementInits(expression.Initializers);
        }

        // The collection is on the stack and stays; each initializer calls its Add method on it.
        private void CompileElementInits(System.Collections.ObjectModel.ReadOnlyCollection<ElementInit> initializers)
        {
            foreach (var initializer in initializers)
            {
                var add = initializer.AddMethod;
                var parameters = add.GetParameters();
                var returnsValue = add.ReturnType != typeof(void);
                if (add.IsStatic || !add.IsExpressionExecutable || parameters.Length != initializer.Arguments.Count ||
                    returnsValue && !ExpressionValidation.IsSupportedInterpreterValue(add.ReturnType))
                {
                    throw Unsupported();
                }
                foreach (var parameter in parameters)
                {
                    if (!IsSupportedMethodParameter(parameter.ParameterType))
                    {
                        throw Unsupported();
                    }
                }
                _instructions.Emit(DupInstruction.Instance);
                foreach (var argument in initializer.Arguments)
                {
                    Compile(argument, asVoid: false);
                }
                _instructions.EmitCall(add, parameters.Length, producesValue: returnsValue);
                if (returnsValue)
                {
                    _instructions.EmitPop();
                }
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
                    if (_captured.Contains(variable))
                    {
                        _instructions.Emit(new InitializeBoxedLocalInstruction(
                            definition.Index,
                            GetDefaultValue(variable.Type)));
                    }
                    else
                    {
                        _instructions.EmitInitializeLocal(
                            definition.Index,
                            GetDefaultValue(variable.Type));
                    }
                }

                // A block typed void discards its last expression's value.
                var discardsResult = asVoid || expression.Type == typeof(void);
                for (var index = 0; index < expression.Expressions.Count; index++)
                {
                    Compile(
                        expression.Expressions[index],
                        discardsResult || index != expression.Expressions.Count - 1);
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

        // A nested lambda: compiled on its own, with a closure of the boxes of the variables it captures, into a
        // delegate of the type it names.
        private void CompileLambda(LambdaExpression lambda)
        {
            var closure = ClosureVariableFinder.Free(lambda);
            var interpreter = CompileInterpreter(lambda, closure);
            foreach (var variable in closure)
            {
                EmitLoadBox(variable);
            }
            _instructions.Emit(new CreateDelegateInstruction(lambda, interpreter, closure.Count));
        }

        // A quoted expression is a value; the variables it captures are read through their boxes when it is quoted.
        private void CompileQuote(UnaryExpression quote)
        {
            var captured = ClosureVariableFinder.Free(quote.Operand);
            System.Collections.Generic.Dictionary<ParameterExpression, QuotedVariable>? variables = null;
            if (captured.Count > 0)
            {
                variables = new();
                foreach (var variable in captured)
                {
                    variables.Add(variable, EmitLoadBox(variable));
                    _instructions.EmitPop();
                }
            }
            _instructions.Emit(new QuoteInstruction(quote.Operand, variables));
        }

        // Invoke of a lambda (Expression.Invoke takes no other delegate expression on NetWasm, whose delegate type
        // only the run knows) runs its body in place: the arguments, in order, become its parameters. A label
        // outside the lambda is out of reach, as it is from a lambda compiled on its own.
        private void CompileInvoke(InvocationExpression invocation, bool asVoid)
        {
            var lambda = ClosureVariableFinder.InlinedLambda(invocation) ?? throw Unsupported();
            foreach (var argument in invocation.Arguments)
            {
                Compile(argument, asVoid: false);
            }
            var parameters = new LocalDefinition[lambda.Parameters.Count];
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = lambda.Parameters[index];
                if (parameter.IsByRef)
                {
                    throw Unsupported();
                }
                ValidateValue(parameter.Type);
                parameters[index] = _locals.Define(parameter);
            }
            var outerLabels = new System.Collections.Generic.Dictionary<LabelTarget, (BranchLabel Label, int? ValueLocal)>(_labels);
            _labels.Clear();
            try
            {
                for (var index = parameters.Length - 1; index >= 0; index--)
                {
                    _instructions.EmitStoreLocal(parameters[index].Index);
                    if (_captured.Contains(lambda.Parameters[index]))
                    {
                        _instructions.Emit(new BoxLocalInstruction(parameters[index].Index));
                    }
                }
                Compile(lambda.Body, asVoid || lambda.ReturnType == typeof(void));
            }
            finally
            {
                _labels.Clear();
                foreach (var (target, label) in outerLabels)
                {
                    _labels.Add(target, label);
                }
                for (var index = parameters.Length - 1; index >= 0; index--)
                {
                    _locals.Undefine(parameters[index]);
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

            // A conditional typed void (IfThen, IfThenElse) discards its branches' values.
            var discardsResult = asVoid || expression.Type == typeof(void);
            var falseLabel = _instructions.MakeLabel();
            var endLabel = _instructions.MakeLabel();
            Compile(expression.Test, asVoid: false);
            _instructions.EmitBranchFalse(falseLabel);
            Compile(expression.IfTrue, discardsResult);
            _instructions.EmitBranch(endLabel, preservesValue: !discardsResult);
            _instructions.MarkLabel(falseLabel);
            Compile(expression.IfFalse, discardsResult);
            _instructions.MarkLabel(endLabel);
        }

        private static void ValidateValue(Type type)
        {
            if (!ExpressionValidation.IsSupportedInterpreterValue(type))
            {
                throw Unsupported();
            }
        }

        private static bool IsPrimitiveNumeric(Type type) =>
            !type.IsEnum && Type.GetTypeCode(type) is
                TypeCode.SByte or TypeCode.Byte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double;

        private static bool IsSupportedMethodParameter(Type type) =>
            !type.IsByRef && !type.IsNullable &&
            ExpressionValidation.IsSupportedInterpreterValue(type);

        private static object? GetDefaultValue(Type type)
        {
            if (type.IsNullable)
            {
                return null;
            }
            if (!type.IsValueType)
            {
                return null;
            }
            if (type.IsEnum)
            {
                return Enum.ToObject(type, 0);
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
                TypeCode.Decimal => 0M,
                // Any other value type's default, boxed: the one element of a new one-element array, which the
                // interpreter's array instructions already have the program keep, rather than
                // GetUninitializedObject, which would keep an uninitialized form of every type a program names.
                _ => Array.CreateInstance(type, 1).GetValue(0),
            };
        }

        private static NotSupportedException Unsupported() =>
            new("This expression shape is not executable in the NetWasm profile.");
    }
}
