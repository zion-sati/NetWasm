using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ExpressionTreeCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void MemberInfoEqualitySupportsNullIdentityAndVirtualOverrides(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = """
            using System.Reflection;

            namespace MemberInfoEqualityFixture;

            internal sealed class ComparableMember(int identity) : MemberInfo
            {
                public override bool Equals(object? value) =>
                    value is ComparableMember other && identity == other.Identity;

                public override int GetHashCode() => identity;

                private int Identity => identity;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    MemberInfo? none = null;
                    MemberInfo first = new ComparableMember(input);
                    MemberInfo same = first;
                    MemberInfo equivalent = new ComparableMember(input);
                    MemberInfo distinct = new ComparableMember(input + 1);

                    if (!(none == null) || none != null || first == null || null == first)
                        return 1;
                    if (!(first == same) || first != same)
                        return 2;
                    if (!(first == equivalent) || first != equivalent)
                        return 3;
                    if (first == distinct || !(first != distinct))
                        return 4;
                    return 42;
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("MemberInfoEqualityFixture", source)
            : assets.CompileSource("MemberInfoEqualityFixture", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "MemberInfoEqualityFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void SourceAdaptedFluentValidationPropertyRulesUseBoundedExpressionMetadata(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = File.ReadAllText(Path.Combine(
            assets.Root,
            "tests",
            "end-to-end",
            "expression-fluentvalidation-consumer",
            "FluentValidationExpressionConsumer.cs"));
        var assembly = optimized
            ? assets.CompileOptimizedSource(
                "FluentValidationExpressionConsumer",
                source)
            : assets.CompileSource(
                "FluentValidationExpressionConsumer",
                source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Tests.ExpressionTrees.FluentValidationSourceAdaptation.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
        Assert.NotEmpty(compilation.Program.NamedMemberDescriptors);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledAccessorLambdasUseBoundedInterpreterAndOrdinaryDelegates(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTreeInterpreterFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionTreeInterpreterFixture;

            public interface IValue
            {
                int Value { get; }
            }

            public delegate int Reader(Model model);

            public readonly struct StructValue : IValue
            {
                private readonly int _value;

                public StructValue(int value) => _value = value;

                public int Value
                {
                    get
                    {
                        GC.Collect();
                        return _value;
                    }
                }
            }

            public class Model : IValue
            {
                public int Field;
                public long LongField;
                public float FloatField;
                public double DoubleField;
                public sbyte SignedByteField;
                public ushort UnsignedShortField;
                public uint UnsignedIntField;
                public string Text;
                public Func<Model, int>? Reenter;
                public Model? Next;

                public Model(int value, string text)
                {
                    Field = value;
                    Text = text;
                }

                public virtual int Value
                {
                    get
                    {
                        GetterCount++;
                        return Reenter is null ? Field : Reenter(Next!);
                    }
                }

                public string Label => Text;

                public void Touch() => Field++;

                public static int GetterCount;
            }

            public sealed class Derived : Model
            {
                public Derived(int value) : base(value, "derived") { }
                public override int Value
                {
                    get
                    {
                        GetterCount++;
                        return Field + 1;
                    }
                }
            }

            public sealed class Throwing
            {
                public int Value => throw new InvalidOperationException("boom");
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Model.GetterCount = 0;
                    Expression<Func<Model, int>> propertyTree = model => model.Value;
                    var property = propertyTree.Compile();
                    if (Model.GetterCount != 0) return 1;
                    if (property(new Model(input, "first")) != input) return 2;
                    if (Model.GetterCount != 1) return 15;

                    var second = propertyTree.Compile(preferInterpretation: true);
                    if (second(new Model(input + 1, "second")) != input + 1)
                        return 3;

                    LambdaExpression erased = propertyTree;
                    var erasedDelegate = (Func<Model, int>)erased.Compile(false);
                    if (erasedDelegate(new Model(input + 2, "third")) != input + 2)
                        return 4;

                    Expression<Reader> customTree = model => model.Value;
                    Reader custom = customTree.Compile();
                    if (custom(new Model(input + 3, "custom")) != input + 3)
                        return 5;

                    Expression<Func<Model, int>> fieldTree = model => model.Field;
                    var field = fieldTree.Compile();
                    if (field(new Model(input + 4, "field")) != input + 4)
                        return 6;

                    Expression<Func<Model, string>> referenceTree = model => model.Label;
                    var reference = referenceTree.Compile();
                    if (reference(new Model(0, "reference")) != "reference")
                        return 7;

                    Expression<Func<IValue, int>> virtualTree = value => value.Value;
                    var virtualAccessor = virtualTree.Compile();
                    if (virtualAccessor(new Derived(input)) != input + 1)
                        return 8;
                    if (virtualAccessor(new StructValue(input + 6)) != input + 6)
                        return 16;

                    var numeric = new Model(0, "numeric")
                    {
                        LongField = long.MinValue + input,
                        FloatField = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
                        DoubleField = BitConverter.Int64BitsToDouble(
                            unchecked((long)0x7ff8000000000042)),
                        SignedByteField = -101,
                        UnsignedShortField = 60001,
                        UnsignedIntField = 0xf0000001u,
                    };
                    Expression<Func<Model, long>> longTree = model => model.LongField;
                    if (longTree.Compile()(numeric) != numeric.LongField)
                        return 17;
                    if (BitConverter.SingleToInt32Bits(
                            ((Expression<Func<Model, float>>)
                                (model => model.FloatField)).Compile()(numeric)) !=
                        unchecked((int)0x80000000))
                        return 18;
                    if (BitConverter.DoubleToInt64Bits(
                            ((Expression<Func<Model, double>>)
                                (model => model.DoubleField)).Compile()(numeric)) !=
                        unchecked((long)0x7ff8000000000042))
                        return 19;
                    if (((Expression<Func<Model, sbyte>>)
                            (model => model.SignedByteField)).Compile()(numeric) != -101)
                        return 20;
                    if (((Expression<Func<Model, ushort>>)
                            (model => model.UnsignedShortField)).Compile()(numeric) != 60001)
                        return 21;
                    if (((Expression<Func<Model, uint>>)
                            (model => model.UnsignedIntField)).Compile()(numeric) != 0xf0000001u)
                        return 22;

                    var tail = new Model(input + 5, "tail");
                    var head = new Model(0, "head")
                    {
                        Reenter = property,
                        Next = tail,
                    };
                    if (property(head) != input + 5)
                        return 9;

                    GC.Collect();
                    if (property(tail) != input + 5)
                        return 10;

                    try
                    {
                        _ = property(null!);
                        return 11;
                    }
                    catch (NullReferenceException)
                    {
                    }

                    Expression<Func<Throwing, int>> throwingTree = value => value.Value;
                    var throwing = throwingTree.Compile();
                    try
                    {
                        _ = throwing(new Throwing());
                        return 12;
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (exception.Message != "boom") return 13;
                    }

                    Expression<Func<int, int>> arithmetic = value => value + 1;
                    if (arithmetic.Compile()(input) != input + 1) return 14;

                    Func<int> ordinaryDelegate = () => input;
                    if (ordinaryDelegate() != input) return 23;

                    Expression<Func<int>> zero = () => input;
                    if (zero.Parameters.Count != 0) return 24;
                    try
                    {
                        _ = zero.Compile();
                        return 25;
                    }
                    catch (NotSupportedException)
                    {
                    }
                    try
                    {
                        _ = ((LambdaExpression)zero).Compile();
                        return 26;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    Expression<Func<Model, Model, int>> binary =
                        (left, right) => left.Field;
                    try
                    {
                        _ = binary.Compile();
                        return 27;
                    }
                    catch (NotSupportedException)
                    {
                    }
                    try
                    {
                        _ = ((LambdaExpression)binary).Compile(true);
                        return 28;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    Expression<Action<Model>> voidTree = model => model.Touch();
                    try
                    {
                        _ = voidTree.Compile();
                        return 29;
                    }
                    catch (NotSupportedException)
                    {
                    }
                    try
                    {
                        _ = ((LambdaExpression)voidTree).Compile();
                        return 30;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    try
                    {
                        _ = Expression.Lambda<object>(
                            propertyTree.Body,
                            propertyTree.Parameters).Compile();
                        return 31;
                    }
                    catch (ArgumentException)
                    {
                    }

                    try
                    {
                        _ = Expression.Lambda<int>(
                            propertyTree.Body,
                            propertyTree.Parameters).Compile();
                        return 32;
                    }
                    catch (ArgumentException)
                    {
                    }

                    try
                    {
                        _ = Expression.Lambda<Delegate>(
                            propertyTree.Body,
                            propertyTree.Parameters).Compile();
                        return 33;
                    }
                    catch (ArgumentException)
                    {
                    }

                    try
                    {
                        _ = Expression.Lambda<MulticastDelegate>(
                            propertyTree.Body,
                            propertyTree.Parameters).Compile();
                        return 34;
                    }
                    catch (ArgumentException)
                    {
                    }

                    return 42;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTreeInterpreterFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
        Assert.NotEmpty(compilation.Program.ObjectArrayDelegateAdapters);
        Assert.NotEmpty(compilation.Program.MemberExecution.Methods);
        Assert.NotEmpty(compilation.Program.MemberExecution.Fields);
        Assert.Contains(
            compilation.Program.CallableMethods.Values,
            method => method.Definition.Name == "Invoke1" &&
                method.Definition.DeclaringType.Assembly.Name == "NetWasm.CoreLib");
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledUnaryLambdasExecuteComputationControlFlowAndLocalBlocks(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTreeControlFlowFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionTreeControlFlowFixture;

            public delegate int Transformer(int value);

            public sealed class Model
            {
                public Model(int number) => Number = number;

                public int Number;
                public Model? Child;
                public bool Left;
                public bool Right;
                public Func<Model, int>? Reenter;

                public int CollectedNumber
                {
                    get
                    {
                        GC.Collect();
                        return Number;
                    }
                }

                public int ReentrantCollectedNumber
                {
                    get
                    {
                        GC.Collect();
                        return Reenter is null ? Number : Reenter(this);
                    }
                }

                public bool CountedLeft
                {
                    get
                    {
                        LeftReads++;
                        return Left;
                    }
                }

                public bool CountedRight
                {
                    get
                    {
                        RightReads++;
                        return Right;
                    }
                }

                public bool ThrowingBoolean =>
                    throw new InvalidOperationException("right operand executed");

                public int ThrowingNumber =>
                    throw new InvalidOperationException("false branch executed");

                public static int LeftReads;
                public static int RightReads;
            }

            public sealed class Holder
            {
                public Model Current;

                public Holder(Model current) => Current = current;
            }

            public sealed class OperatorValue
            {
                public OperatorValue(int value) => Value = value;

                public int Value;

                public static OperatorValue operator +(
                    OperatorValue left,
                    OperatorValue right) => new(left.Value + right.Value);

                public static explicit operator int(OperatorValue value) =>
                    value.Value;
            }

            public readonly struct StructValue
            {
                public StructValue(int value) => Value = value;

                public int Value { get; }
            }

            public static class EntryPoint
            {
                private static int DirectDoubleToInt(double value) => (int)value;

                public static int Run(int input)
                {
                    Expression<Func<int, int>> identityTree = value => value;
                    if (identityTree.Compile()(input) != input) return 1;

                    Expression<Func<int, int>> constantTree = value => 42;
                    if (constantTree.Compile()(input) != 42) return 2;
                    Expression<Transformer> customTree = value => value + 1;
                    if (customTree.Compile()(input) != 42) return 26;

                    Expression<Func<int, int>> arithmeticTree =
                        value => value > 0 ? value + 1 : -value;
                    var arithmetic = arithmeticTree.Compile();
                    if (arithmetic(input) != 42 || arithmetic(-41) != 41 ||
                        arithmetic(int.MaxValue) != int.MinValue) return 3;
                    Expression<Func<int, int>> nestedConditionalTree =
                        value => 1 + (value > 0 ? value : -value);
                    var nestedConditional = nestedConditionalTree.Compile();
                    if (nestedConditional(41) != 42 || nestedConditional(-41) != 42)
                        return 30;

                    Expression<Func<long, long>> longAddTree = value => value + 1L;
                    if (longAddTree.Compile()(long.MaxValue) != long.MinValue) return 4;
                    Expression<Func<uint, uint>> uintAddTree = value => value + 1U;
                    if (uintAddTree.Compile()(uint.MaxValue) != 0U) return 5;
                    Expression<Func<ulong, bool>> ulongLessTree = value => value < 10UL;
                    if (!ulongLessTree.Compile()(9UL) ||
                        ulongLessTree.Compile()(ulong.MaxValue)) return 6;

                    Expression<Func<float, float>> singleNegateTree = value => -value;
                    if (BitConverter.SingleToInt32Bits(singleNegateTree.Compile()(0F)) !=
                        unchecked((int)0x80000000)) return 7;
                    var nan = BitConverter.Int64BitsToDouble(
                        unchecked((long)0x7ff8000000000042));
                    var doubleParameter = Expression.Parameter(typeof(double), "value");
                    var nanComparisonTree = Expression.Lambda<Func<double, bool>>(
                        Expression.LessThan(
                            doubleParameter,
                            Expression.Constant(nan)),
                        doubleParameter);
                    if (nanComparisonTree.Compile()(1D)) return 8;

                    Expression<Func<double, int>> toIntTree = value => (int)value;
                    var toInt = toIntTree.Compile();
                    if (toInt(nan) != DirectDoubleToInt(nan) ||
                        toInt(double.PositiveInfinity) !=
                            DirectDoubleToInt(double.PositiveInfinity) ||
                        toInt(-123.75D) != DirectDoubleToInt(-123.75D)) return 9;
                    Expression<Func<int, double>> toDoubleTree = value => (double)value;
                    if (toDoubleTree.Compile()(16_777_217) != (double)16_777_217)
                        return 10;

                    Model.LeftReads = 0;
                    Model.RightReads = 0;
                    Expression<Func<Model, bool>> shortCircuitTree =
                        model => model.CountedLeft && model.ThrowingBoolean;
                    var shortCircuit = shortCircuitTree.Compile();
                    if (shortCircuit(new Model(0) { Left = false }) ||
                        Model.LeftReads != 1 || Model.RightReads != 0) return 11;

                    Expression<Func<Model, bool>> bothTree =
                        model => model.CountedLeft && model.CountedRight;
                    var both = bothTree.Compile();
                    if (!both(new Model(0) { Left = true, Right = true }) ||
                        Model.LeftReads != 2 || Model.RightReads != 1) return 12;

                    Expression<Func<Model, int>> conditionalTree =
                        model => model.CountedLeft
                            ? model.ThrowingNumber
                            : model.Number;
                    if (conditionalTree.Compile()(new Model(42) { Left = false }) != 42)
                        return 13;

                    var parameter = Expression.Parameter(typeof(int), "value");
                    var local = Expression.Variable(typeof(int), "local");
                    var blockTree = Expression.Lambda<Func<int, int>>(
                        Expression.Block(
                            new[] { local },
                            Expression.Assign(
                                local,
                                Expression.Add(parameter, Expression.Constant(1))),
                            Expression.Condition(
                                Expression.GreaterThan(local, Expression.Constant(0)),
                                local,
                                Expression.Negate(local))),
                        parameter);
                    var block = blockTree.Compile();
                    if (block(41) != 42 || block(-42) != 41) return 14;

                    var shared = Expression.Variable(typeof(int), "shared");
                    var shadowTree = Expression.Lambda<Func<int, int>>(
                        Expression.Block(
                            new[] { shared },
                            Expression.Assign(shared, parameter),
                            Expression.Add(
                                Expression.Block(
                                    new[] { shared },
                                    Expression.Assign(shared, Expression.Constant(1)),
                                    shared),
                                shared)),
                        parameter);
                    if (shadowTree.Compile()(41) != 42) return 15;

                    var reset = Expression.Variable(typeof(int), "reset");
                    var initializeTree = Expression.Lambda<Func<int, int>>(
                        Expression.Block(
                            new[] { reset },
                            Expression.Condition(
                                Expression.GreaterThan(parameter, Expression.Constant(0)),
                                Expression.Block(
                                    Expression.Assign(reset, parameter),
                                    Expression.Empty()),
                                Expression.Empty()),
                            reset),
                        parameter);
                    var initialize = initializeTree.Compile();
                    if (initialize(42) != 42 || initialize(0) != 0) return 16;

                    var assignParameterTree = Expression.Lambda<Func<int, int>>(
                        Expression.Assign(parameter, Expression.Constant(42)),
                        parameter);
                    if (assignParameterTree.Compile()(0) != 42) return 17;

                    var defaultIntTree = Expression.Lambda<Func<int, int>>(
                        Expression.Default(typeof(int)),
                        parameter);
                    if (defaultIntTree.Compile()(42) != 0) return 18;
                    var modelParameter = Expression.Parameter(typeof(Model), "model");
                    var defaultReferenceTree = Expression.Lambda<Func<Model, Model>>(
                        Expression.Default(typeof(Model)),
                        modelParameter);
                    if (defaultReferenceTree.Compile()(new Model(1)) is not null) return 19;

                    Expression<Func<Model, int>> numberTree = model => model.Number;
                    var reentrant = numberTree.Compile();
                    Expression<Func<Model, int>> reentrantTree =
                        model => model.ReentrantCollectedNumber;
                    var referenceLocal = Expression.Variable(typeof(Model), "local");
                    var referenceParameter = Expression.Parameter(typeof(Model), "model");
                    var referenceBlockTree = Expression.Lambda<Func<Model, int>>(
                        Expression.Block(
                            new[] { referenceLocal },
                            Expression.Assign(referenceLocal, referenceParameter),
                            Expression.MakeMemberAccess(
                                referenceLocal,
                                ((MemberExpression)reentrantTree.Body).Member)),
                        referenceParameter);
                    var referenceModel = new Model(42) { Reenter = reentrant };
                    if (referenceBlockTree.Compile()(referenceModel) != 42) return 27;

                    var holder = new Holder(new Model(0)
                    {
                        Child = new Model(1),
                    });
                    Expression<Func<int, int>> capturedTree =
                        value => value + holder.Current.Child!.CollectedNumber;
                    var captured = capturedTree.Compile();
                    holder.Current.Child = new Model(40);
                    if (captured(2) != 42) return 20;

                    Expression<Func<int, int>> unselectedUnsupportedTree =
                        value => value > 0
                            ? value
                            : BitConverter.SingleToInt32Bits((float)value);
                    try
                    {
                        _ = unselectedUnsupportedTree.Compile();
                        return 21;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    Expression<Func<OperatorValue, OperatorValue>> operatorTree =
                        value => value + value;
                    try
                    {
                        _ = operatorTree.Compile();
                        return 22;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    Expression<Func<int, Func<int>>> nestedTree = value => () => value;
                    try
                    {
                        _ = nestedTree.Compile();
                        return 23;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    Expression<Func<StructValue, int>> structMemberTree =
                        value => value.Value;
                    try
                    {
                        _ = structMemberTree.Compile();
                        return 24;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    Expression<Func<OperatorValue, int>> conversionTree =
                        value => (int)value;
                    try
                    {
                        _ = conversionTree.Compile();
                        return 28;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    Expression<Func<int, Model>> constructorTree =
                        value => new Model(value);
                    try
                    {
                        _ = constructorTree.Compile();
                        return 29;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    var unbound = Expression.Parameter(typeof(int), "unbound");
                    var unboundTree = Expression.Lambda<Func<int, int>>(
                        unbound,
                        parameter);
                    try
                    {
                        _ = unboundTree.Compile();
                        return 25;
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return 42;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTreeControlFlowFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
        Assert.NotEmpty(compilation.Program.ObjectArrayDelegateAdapters);
        Assert.NotEmpty(compilation.Program.MemberExecution.Methods);
        Assert.NotEmpty(compilation.Program.MemberExecution.Fields);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledMethodCallsPreserveArgumentsDispatchInitializationAndExceptions(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTreeMethodCallFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionTreeMethodCallFixture;

            public static class Tracker
            {
                public static int Events;
                public static int StaticInitializations;
                public static int FailedInitializations;
                public static int GenericInitializations;

                public static void Record(int value) =>
                    Events = Events * 10 + value;
            }

            public sealed class Model
            {
                public Model(int number) => Number = number;

                public int Number;
                public Model? Child;
                public long LongValue;
                public float FloatValue;

                public Model NextReference
                {
                    get
                    {
                        Tracker.Record(1);
                        return this;
                    }
                }

                public int NextInt
                {
                    get
                    {
                        Tracker.Record(2);
                        return 1;
                    }
                }

                public double NextDouble
                {
                    get
                    {
                        Tracker.Record(3);
                        return 1D;
                    }
                }

                public long NextLong
                {
                    get
                    {
                        Tracker.Record(7);
                        return LongValue;
                    }
                }

                public float NextFloat
                {
                    get
                    {
                        Tracker.Record(8);
                        return FloatValue;
                    }
                }

                public int Combine(int left, double right)
                {
                    Tracker.Record(6);
                    GC.Collect();
                    return Number + left + (int)right;
                }

                public int ThrowDirect() =>
                    throw new ArgumentException("direct target failure");
            }

            public static class StaticTarget
            {
                static StaticTarget()
                {
                    Tracker.StaticInitializations++;
                    Tracker.Record(4);
                    GC.Collect();
                }

                public static int Combine(
                    Model model,
                    int left,
                    double right)
                {
                    Tracker.Record(5);
                    GC.Collect();
                    return model.Number + left + (int)right;
                }

                public static T Identity<T>(T value) => value;

                public static Model Preserve(Model model)
                {
                    GC.Collect();
                    return model;
                }

                public static long RoundTrip(long value, float adjustment)
                {
                    Tracker.Record(9);
                    GC.Collect();
                    return value + (long)adjustment;
                }
            }

            public static class FailingTarget
            {
                static FailingTarget()
                {
                    Tracker.FailedInitializations++;
                    throw new InvalidOperationException("static initialization failure");
                }

                public static int Get() => 42;
            }

            public static class GenericTarget<T>
            {
                static GenericTarget()
                {
                    Tracker.GenericInitializations++;
                    GC.Collect();
                }

                public static int Add(int value, int delta) => value + delta;

                public static T Identity(T value) => value;
            }

            public interface ICalculator
            {
                int Add(int value);
            }

            public sealed class Calculator : ICalculator
            {
                public int Add(int value) => 40 + value;
            }

            public readonly struct BoxedCalculator : ICalculator
            {
                private readonly int _value;

                public BoxedCalculator(int value) => _value = value;

                public int Add(int value) => _value + value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var model = new Model(40)
                    {
                        LongValue = long.MinValue + 1000,
                        FloatValue = 1.5F,
                    };
                    Tracker.Events = 0;
                    Expression<Func<Model, int>> staticTree = value =>
                        StaticTarget.Combine(
                            value.NextReference,
                            value.NextInt,
                            value.NextDouble);
                    var staticCall = staticTree.Compile();
                    if (staticCall(model) != 42 ||
                        Tracker.Events != 12345 ||
                        Tracker.StaticInitializations != 1) return 1;
                    if (staticCall(model) != 42 ||
                        Tracker.Events != 123451235 ||
                        Tracker.StaticInitializations != 1) return 2;

                    Tracker.Events = 0;
                    Expression<Func<Model, int>> instanceTree = value =>
                        value.Combine(value.NextInt, value.NextDouble);
                    if (instanceTree.Compile()(model) != 42 ||
                        Tracker.Events != 236) return 3;

                    Tracker.Events = 0;
                    Expression<Func<Model, int>> nullTree = value =>
                        value.Child!.Combine(value.NextInt, value.NextDouble);
                    try
                    {
                        _ = nullTree.Compile()(model);
                        return 4;
                    }
                    catch (NullReferenceException)
                    {
                    }
                    if (Tracker.Events != 23) return 5;

                    Expression<Func<ICalculator, int>> virtualTree = value =>
                        value.Add(2);
                    var virtualCall = virtualTree.Compile();
                    if (virtualCall(new Calculator()) != 42) return 6;
                    ICalculator boxed = new BoxedCalculator(40);
                    if (virtualCall(boxed) != 42) return 7;

                    Expression<Func<int, int>> genericTypeTree = value =>
                        GenericTarget<int>.Add(value, 1);
                    var genericTypeCall = genericTypeTree.Compile();
                    if (genericTypeCall(input) != 42 ||
                        genericTypeCall(input) != 42 ||
                        Tracker.GenericInitializations != 1) return 8;

                    Expression<Func<long, long>> secondGenericTypeTree = value =>
                        GenericTarget<long>.Identity(value + 1L);
                    if (secondGenericTypeTree.Compile()(41L) != 42L ||
                        Tracker.GenericInitializations != 2) return 14;

                    Tracker.Events = 0;
                    Expression<Func<Model, Model>> referenceResultTree = value =>
                        StaticTarget.Preserve(value.NextReference);
                    var preserved = referenceResultTree.Compile()(model);
                    if (!object.ReferenceEquals(preserved, model) ||
                        preserved.Number != 40 || Tracker.Events != 1) return 15;

                    Tracker.Events = 0;
                    Expression<Func<Model, long>> scalarTree = value =>
                        StaticTarget.RoundTrip(value.NextLong, value.NextFloat);
                    if (scalarTree.Compile()(model) != model.LongValue + 1L ||
                        Tracker.Events != 789) return 16;

                    Expression<Func<int, int>> genericMethodTree = value =>
                        StaticTarget.Identity<int>(value + 1);
                    if (genericMethodTree.Compile()(input) != 42) return 9;

                    Expression<Func<Model, int>> throwingTree = value =>
                        value.ThrowDirect();
                    try
                    {
                        _ = throwingTree.Compile()(model);
                        return 10;
                    }
                    catch (ArgumentException)
                    {
                    }

                    Expression<Func<int, int>> failedInitializationTree = value =>
                        FailingTarget.Get();
                    var failedInitialization = failedInitializationTree.Compile();
                    for (var index = 0; index < 2; index++)
                    {
                        try
                        {
                            _ = failedInitialization(input);
                            return 11;
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                    if (Tracker.FailedInitializations != 1) return 12;

                    Expression<Func<float, int>> intrinsicMethod = value =>
                        BitConverter.SingleToInt32Bits(value);
                    var intrinsicParameter = Expression.Parameter(
                        typeof(float),
                        "value");
                    var excludedIntrinsic = Expression.Lambda<Func<float, int>>(
                        Expression.Condition(
                            Expression.Constant(false),
                            Expression.Call(
                                null,
                                ((MethodCallExpression)intrinsicMethod.Body).Method,
                                intrinsicParameter),
                            Expression.Constant(42)),
                        intrinsicParameter);
                    try
                    {
                        _ = excludedIntrinsic.Compile();
                        return 13;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    return 42;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTreeMethodCallFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
        Assert.Contains(
            compilation.Program.MemberExecution.Methods.Values,
            execution => execution.Descriptor.Definition.Name == "Combine");
        Assert.Contains(
            compilation.Program.MemberExecution.Methods.Values,
            execution => execution.Descriptor.Definition.Name == "Identity");
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledNumericConversionsMatchDesktopInterpretation(
        bool optimized,
        WasmTarget target)
    {
        var signedByte = ((System.Linq.Expressions.Expression<Func<int, sbyte>>)
                (value => (sbyte)value))
            .Compile(preferInterpretation: true)(255);
        var unsignedByte = ((System.Linq.Expressions.Expression<Func<int, byte>>)
                (value => (byte)value))
            .Compile(preferInterpretation: true)(511);
        var signedShort = ((System.Linq.Expressions.Expression<Func<int, short>>)
                (value => (short)value))
            .Compile(preferInterpretation: true)(65_535);
        var unsignedShort = ((System.Linq.Expressions.Expression<Func<int, ushort>>)
                (value => (ushort)value))
            .Compile(preferInterpretation: true)(-1);
        var unsignedInt = ((System.Linq.Expressions.Expression<Func<int, uint>>)
                (value => (uint)value))
            .Compile(preferInterpretation: true)(-1);
        var unsignedLong = ((System.Linq.Expressions.Expression<Func<long, ulong>>)
                (value => (ulong)value))
            .Compile(preferInterpretation: true)(-1L);
        var wrappedInt = ((System.Linq.Expressions.Expression<Func<ulong, int>>)
                (value => (int)value))
            .Compile(preferInterpretation: true)(ulong.MaxValue);
        var wrappedShort = ((System.Linq.Expressions.Expression<Func<uint, short>>)
                (value => (short)value))
            .Compile(preferInterpretation: true)(0xffff8001U);
        var singleBits = BitConverter.SingleToInt32Bits(
            ((System.Linq.Expressions.Expression<Func<int, float>>)
                    (value => (float)value))
                .Compile(preferInterpretation: true)(16_777_217));
        var doubleBits = BitConverter.DoubleToInt64Bits(
            ((System.Linq.Expressions.Expression<Func<long, double>>)
                    (value => (double)value))
                .Compile(preferInterpretation: true)(9_007_199_254_740_993L));
        var convertedInt = ((System.Linq.Expressions.Expression<Func<double, int>>)
                (value => (int)value))
            .Compile(preferInterpretation: true)(-123.75D);
        var convertedLong = ((System.Linq.Expressions.Expression<Func<double, long>>)
                (value => (long)value))
            .Compile(preferInterpretation: true)(-123_456_789.75D);
        var narrowedSingleBits = BitConverter.SingleToInt32Bits(
            ((System.Linq.Expressions.Expression<Func<double, float>>)
                    (value => (float)value))
                .Compile(preferInterpretation: true)(1.0000000596046448D));

        using var assets = TestAssets.Create();
        var source = FormattableString.Invariant(
            $$"""
            using System;
            using System.Linq.Expressions;

            namespace ExpressionTreeNumericOracleFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<int, sbyte>> toSByte = value => (sbyte)value;
                    if (toSByte.Compile()(255) != {{signedByte}}) return 1;
                    Expression<Func<int, byte>> toByte = value => (byte)value;
                    if (toByte.Compile()(511) != {{unsignedByte}}) return 2;
                    Expression<Func<int, short>> toShort = value => (short)value;
                    if (toShort.Compile()(65_535) != {{signedShort}}) return 3;
                    Expression<Func<int, ushort>> toUShort = value => (ushort)value;
                    if (toUShort.Compile()(-1) != {{unsignedShort}}) return 4;
                    Expression<Func<int, uint>> toUInt = value => (uint)value;
                    if (toUInt.Compile()(-1) != {{unsignedInt}}U) return 5;
                    Expression<Func<long, ulong>> toULong = value => (ulong)value;
                    if (toULong.Compile()(-1L) != {{unsignedLong}}UL) return 6;
                    Expression<Func<ulong, int>> toInt = value => (int)value;
                    if (toInt.Compile()(ulong.MaxValue) != {{wrappedInt}}) return 7;
                    Expression<Func<uint, short>> uintToShort = value => (short)value;
                    if (uintToShort.Compile()(0xffff8001U) != {{wrappedShort}}) return 8;

                    Expression<Func<int, float>> toSingle = value => (float)value;
                    if (BitConverter.SingleToInt32Bits(toSingle.Compile()(16_777_217)) !=
                        {{singleBits}}) return 9;
                    Expression<Func<long, double>> toDouble = value => (double)value;
                    if (BitConverter.DoubleToInt64Bits(
                            toDouble.Compile()(9_007_199_254_740_993L)) !=
                        {{doubleBits}}L) return 10;
                    Expression<Func<double, int>> doubleToInt = value => (int)value;
                    if (doubleToInt.Compile()(-123.75D) != {{convertedInt}}) return 11;
                    Expression<Func<double, long>> doubleToLong = value => (long)value;
                    if (doubleToLong.Compile()(-123_456_789.75D) != {{convertedLong}}L)
                        return 12;
                    Expression<Func<double, float>> doubleToSingle = value => (float)value;
                    if (BitConverter.SingleToInt32Bits(
                            doubleToSingle.Compile()(1.0000000596046448D)) !=
                        {{narrowedSingleBits}}) return 13;

                    var parameter = Expression.Parameter(typeof(int), "unused");
                    var defaultSByte = Expression.Lambda<Func<int, sbyte>>(
                        Expression.Default(typeof(sbyte)),
                        parameter).Compile();
                    var defaultUShort = Expression.Lambda<Func<int, ushort>>(
                        Expression.Default(typeof(ushort)),
                        parameter).Compile();
                    var defaultSingle = Expression.Lambda<Func<int, float>>(
                        Expression.Default(typeof(float)),
                        parameter).Compile();
                    var defaultDouble = Expression.Lambda<Func<int, double>>(
                        Expression.Default(typeof(double)),
                        parameter).Compile();
                    if (defaultSByte(input) != 0 || defaultUShort(input) != 0 ||
                        BitConverter.SingleToInt32Bits(defaultSingle(input)) != 0 ||
                        BitConverter.DoubleToInt64Bits(defaultDouble(input)) != 0)
                        return 14;

                    return 42;
                }
            }
            """);
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTreeNumericOracleFixture",
            source,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTreeNumericOracleFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0,
            target,
            compilation.StaticDataEnd));
        Assert.NotEmpty(compilation.Program.ObjectArrayDelegateAdapters);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerConstructsAndInspectsParameterConstantAndGenericLambda(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTreeFoundationFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionTreeFoundationFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var parameter = Expression.Parameter(typeof(int), "value");
                    var replacement = Expression.Variable(typeof(int), "replacement");
                    var source = new[] { parameter };
                    var lambda = Expression.Lambda<Func<int, int>>(
                        parameter,
                        "identity",
                        tailCall: true,
                        source);
                    source[0] = replacement;

                    if (parameter.NodeType != ExpressionType.Parameter ||
                        parameter.Type != typeof(int) || parameter.Name != "value" ||
                        parameter.IsByRef) return 1;
                    if (lambda.NodeType != ExpressionType.Lambda ||
                        lambda.Type != typeof(Func<int, int>) ||
                        lambda.ReturnType != typeof(int) ||
                        lambda.Body != parameter || lambda.Name != "identity" ||
                        !lambda.TailCall || lambda.Parameters.Count != 1 ||
                        lambda.Parameters[0] != parameter) return 2;

                    var constant = Expression.Constant(input, typeof(int));
                    if (constant.NodeType != ExpressionType.Constant ||
                        constant.Type != typeof(int) ||
                        constant.Value is not int value || value != input) return 3;
                    var nullString = Expression.Constant(null, typeof(string));
                    if (nullString.Type != typeof(string) ||
                        nullString.Value is not null) return 4;

                    if (lambda.Update(parameter, new[] { parameter }) != lambda)
                        return 5;
                    var updated = lambda.Update(constant, new[] { parameter });
                    if (updated == lambda || updated.Body != constant ||
                        updated.Parameters[0] != parameter ||
                        updated.Name != "identity" || !updated.TailCall) return 6;

                    bool rejectedCount = false;
                    try
                    {
                        _ = Expression.Lambda<Func<int, int>>(parameter);
                    }
                    catch (ArgumentException)
                    {
                        rejectedCount = true;
                    }
                    return rejectedCount ? 42 : 7;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTreeFoundationFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresDelegateInvoke);
        var invoke = Assert.Single(
            compilation.Program.DelegateInvokeDescriptors.Values,
            candidate => candidate.DeclaringType.Shape ==
                    CliTypeShape.GenericInstantiation &&
                candidate.DeclaringType.ElementType!.FullName == "System.Func`2" &&
                candidate.DeclaringType.TypeArguments.Length == 2 &&
                candidate.DeclaringType.TypeArguments.All(argument =>
                    argument.StackKind == CliValueKind.I4));
        Assert.Contains(invoke.CanonicalName, compilation.Program.MethodDescriptors);
        Assert.DoesNotContain(
            compilation.Program.Methods.Values,
            method => method.Method.CanonicalName == invoke.CanonicalName);
        Assert.DoesNotContain(
            compilation.Program.ConstructedMethods.Values,
            method => method.Method.CanonicalName == invoke.CanonicalName);
        Assert.DoesNotContain(
            compilation.Program.ConstructedStaticInitializers,
            identity => identity.Contains("System.Func`2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void RoslynTreesMaterializeBoundedMemberConstructorAndCallDescriptors(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTreeMemberFixture",
            """
            using System;
            using System.Linq.Expressions;
            using System.Reflection;

            namespace ExpressionTreeMemberFixture;

            public sealed class Model
            {
                public int Field;
                public Model(int value) { Field = value; }
                public int Property => Field;
                public int Add(int value) => Field + value;
                public static int Twice(int value) => value * 2;
            }

            public static class GenericBox<T>
            {
                public static T Identity(T value) => value;
            }

            public static class GenericMethods
            {
                public static T Identity<T>(T value) => value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<Model, int>> propertyTree = model => model.Property;
                    Expression<Func<Model, int>> fieldTree = model => model.Field;
                    Expression<Func<int, Model>> constructorTree = value => new Model(value);
                    Expression<Func<Model, int>> instanceTree = model => model.Add(1);
                    Expression<Func<int, int>> staticTree = value => Model.Twice(value);
                    Expression<Func<int, int>> genericOwnerTree =
                        value => GenericBox<int>.Identity(value);
                    Expression<Func<int, int>> genericMethodTree =
                        value => GenericMethods.Identity<int>(value);

                    var property = propertyTree.Body as MemberExpression;
                    if (property is null || property.Member is not PropertyInfo ||
                        property.Expression != propertyTree.Parameters[0] ||
                        property.Type != typeof(int)) return 1;
                    var field = fieldTree.Body as MemberExpression;
                    if (field is null || field.Member is not FieldInfo ||
                        field.Expression != fieldTree.Parameters[0] ||
                        field.Type != typeof(int)) return 2;
                    var created = constructorTree.Body as NewExpression;
                    if (created is null || created.Constructor.DeclaringType != typeof(Model) ||
                        created.Arguments.Count != 1 ||
                        created.Arguments[0] != constructorTree.Parameters[0] ||
                        created.Type != typeof(Model) || created.Members is not null) return 3;
                    var instance = instanceTree.Body as MethodCallExpression;
                    if (instance is null || instance.Object != instanceTree.Parameters[0] ||
                        instance.Method.IsStatic || instance.Arguments.Count != 1 ||
                        instance.Type != typeof(int)) return 4;
                    var @static = staticTree.Body as MethodCallExpression;
                    if (@static is null || @static.Object is not null ||
                        !@static.Method.IsStatic || @static.Arguments.Count != 1) return 5;
                    var genericOwner = genericOwnerTree.Body as MethodCallExpression;
                    if (genericOwner is null ||
                        genericOwner.Method.DeclaringType != typeof(GenericBox<int>) ||
                        genericOwner.Method.ContainsGenericParameters) return 6;
                    var genericMethod = genericMethodTree.Body as MethodCallExpression;
                    if (genericMethod is null || !genericMethod.Method.IsGenericMethod ||
                        genericMethod.Method.IsGenericMethodDefinition ||
                        genericMethod.Method.ContainsGenericParameters) return 7;

                    if (property.Update(property.Expression) != property ||
                        created.Update(created.Arguments) != created ||
                        instance.Update(instance.Object, instance.Arguments) != instance)
                        return 8;
                    return 42;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTreeMemberFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0,
            target,
            compilation.StaticDataEnd));
        Assert.DoesNotContain(
            compilation.Program.Methods.Values,
            method => method.Method.Definition.Name is
                "get_Property" or "Add" or "Twice" or ".ctor" &&
                method.Method.Definition.Key.Assembly.Name ==
                    "ExpressionTreeMemberFixture");
        Assert.DoesNotContain(
            compilation.Program.ConstructedMethods.Values,
            method => method.Method.Definition.Name == "Identity" &&
                method.Method.Definition.Key.Assembly.Name ==
                    "ExpressionTreeMemberFixture");
        Assert.DoesNotContain(
            compilation.Program.Fields,
            field => field.Assembly.Name == "ExpressionTreeMemberFixture");
        Assert.DoesNotContain(
            compilation.Program.ConstructedStaticInitializers,
            identity => identity.Contains(
                "ExpressionTreeMemberFixture",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void RoslynTreesMaterializeOperatorsConditionQuoteBlockAndInvoke(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTreeConstructionFixture",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq.Expressions;
            using System.Reflection;

            namespace ExpressionTreeConstructionFixture;

            public sealed class Model
            {
                public int Value;

                public Model(int value)
                {
                    Value = value;
                }

                public static Model operator +(Model value, int increment) =>
                    new(value.Value + increment);

                public static explicit operator int(Model value) => value.Value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<int, int>> arithmetic = value => value + 1;
                    Expression<Func<int, int>> conditional =
                        value => value > 0 && value < 10 ? value : -value;
                    Expression<Func<Model, Model>> customOperator =
                        model => model + 1;
                    Expression<Func<Model, int>> conversion =
                        model => (int)model;
                    Expression<Func<int, int>> quote =
                        value => Accept(inner => inner + 1, value);
                    Expression<Func<int, int>> closure =
                        value => value + input;
                    Expression<Func<object, object>> objectCall =
                        value => Identity(value);
                    Expression<Func<int, Model>> constructor =
                        value => new Model(value);

                    var add = arithmetic.Body as BinaryExpression;
                    if (add is null || add.NodeType != ExpressionType.Add ||
                        add.Left != arithmetic.Parameters[0] ||
                        add.Right is not ConstantExpression addend ||
                        addend.Value is not int one || one != 1 ||
                        add.Type != typeof(int) || add.Method is not null ||
                        add.IsLifted || add.IsLiftedToNull ||
                        add.Conversion is not null ||
                        add.Update(add.Left, add.Conversion, add.Right) != add)
                        return 1;

                    var choice = conditional.Body as ConditionalExpression;
                    var and = choice?.Test as BinaryExpression;
                    var greater = and?.Left as BinaryExpression;
                    var less = and?.Right as BinaryExpression;
                    var negative = choice?.IfFalse as UnaryExpression;
                    if (choice is null || and is null || greater is null ||
                        less is null || negative is null ||
                        choice.NodeType != ExpressionType.Conditional ||
                        choice.Type != typeof(int) ||
                        and.NodeType != ExpressionType.AndAlso ||
                        greater.NodeType != ExpressionType.GreaterThan ||
                        less.NodeType != ExpressionType.LessThan ||
                        negative.NodeType != ExpressionType.Negate ||
                        negative.Operand != conditional.Parameters[0] ||
                        negative.Method is not null ||
                        choice.Update(choice.Test, choice.IfTrue, choice.IfFalse) != choice)
                        return 2;

                    var userAdd = customOperator.Body as BinaryExpression;
                    if (userAdd is null || userAdd.Method is null ||
                        !userAdd.Method.IsStatic ||
                        userAdd.Method.DeclaringType != typeof(Model) ||
                        userAdd.Type != typeof(Model))
                        return 3;

                    var cast = conversion.Body as UnaryExpression;
                    if (cast is null || cast.NodeType != ExpressionType.Convert ||
                        cast.Method is null || !cast.Method.IsStatic ||
                        cast.Type != typeof(int) ||
                        cast.Update(cast.Operand) != cast)
                        return 4;

                    var quotedCall = quote.Body as MethodCallExpression;
                    var quoted = quotedCall?.Arguments[0] as UnaryExpression;
                    var innerLambda = quoted?.Operand as LambdaExpression;
                    if (quotedCall is null || quoted is null || innerLambda is null ||
                        quoted.NodeType != ExpressionType.Quote ||
                        quoted.Type != typeof(Expression<Func<int, int>>) ||
                        innerLambda.Body is not BinaryExpression ||
                        innerLambda.Parameters.Count != 1)
                        return 5;

                    var closureAdd = closure.Body as BinaryExpression;
                    var captured = closureAdd?.Right as MemberExpression;
                    var closureObject = captured?.Expression as ConstantExpression;
                    if (closureAdd is null || captured is null ||
                        captured.Member is not FieldInfo || closureObject is null ||
                        closureObject.Value is null)
                        return 6;

                    var value = Expression.Parameter(typeof(int), "value");
                    var local = Expression.Variable(typeof(int), "local");
                    var inner = Expression.Lambda<Func<int, int>>(
                        Expression.Add(value, Expression.Constant(1)),
                        value);
                    var assign = Expression.Assign(local, Expression.Constant(input));
                    var invoke = Expression.Invoke(inner, local);
                    var sourceVariables = new[] { local };
                    var sourceExpressions = new Expression[] { assign, invoke };
                    var block = Expression.Block(sourceVariables, sourceExpressions);
                    sourceVariables[0] = value;
                    sourceExpressions[0] = invoke;

                    if (block.NodeType != ExpressionType.Block ||
                        block.Type != typeof(int) || block.Variables.Count != 1 ||
                        block.Variables[0] != local || block.Expressions.Count != 2 ||
                        block.Expressions[0] != assign || block.Result != invoke ||
                        assign.NodeType != ExpressionType.Assign ||
                        invoke.NodeType != ExpressionType.Invoke ||
                        invoke.Expression != inner || invoke.Arguments.Count != 1 ||
                        invoke.Arguments[0] != local || invoke.Type != typeof(int) ||
                        block.Update(block.Variables, block.Expressions) != block ||
                        invoke.Update(invoke.Expression, invoke.Arguments) != invoke)
                        return 7;

                    var empty = Expression.Empty();
                    if (empty.NodeType != ExpressionType.Default ||
                        empty.Type != typeof(void) ||
                        Expression.Default(typeof(int)).Type != typeof(int))
                        return 8;

                    var nullableNull = Expression.Constant(null, typeof(int?));
                    if (nullableNull.Type != typeof(int?) ||
                        nullableNull.Value is not null)
                        return 21;
                    var rejectedNonNullNullable = false;
                    try
                    {
                        _ = Expression.Constant(1, typeof(int?));
                    }
                    catch (NotSupportedException)
                    {
                        rejectedNonNullNullable = true;
                    }
                    if (!rejectedNonNullNullable)
                        return 24;

                    var voidLambda = Expression.Lambda<Action>(
                        Expression.Constant(1));
                    if (voidLambda.ReturnType != typeof(void) ||
                        voidLambda.Body.Type != typeof(int))
                        return 9;

                    MethodCallExpression automaticallyQuotedCall;
                    try
                    {
                        automaticallyQuotedCall = Expression.Call(
                            null,
                            quotedCall.Method,
                            inner,
                            Expression.Constant(0));
                    }
                    catch (ArgumentException)
                    {
                        return 18;
                    }
                    if (automaticallyQuotedCall.Arguments[0] is not UnaryExpression
                        { NodeType: ExpressionType.Quote })
                        return 10;
                    Expression<Func<Expression<Func<int, int>>>>
                        automaticallyQuotedLambda;
                    try
                    {
                        automaticallyQuotedLambda =
                            Expression.Lambda<Func<Expression<Func<int, int>>>>(
                                inner);
                    }
                    catch (ArgumentException)
                    {
                        return 19;
                    }
                    if (automaticallyQuotedLambda.Body is not UnaryExpression
                        { NodeType: ExpressionType.Quote })
                        return 11;
                    Expression<Func<LambdaExpression>> automaticallyQuotedBase;
                    try
                    {
                        automaticallyQuotedBase =
                            Expression.Lambda<Func<LambdaExpression>>(inner);
                    }
                    catch (ArgumentException)
                    {
                        return 20;
                    }
                    if (automaticallyQuotedBase.Body is not UnaryExpression
                        { NodeType: ExpressionType.Quote })
                        return 11;

                    var explicitQuote = Expression.Quote(inner);
                    var quotedInvocation = Expression.Invoke(
                        explicitQuote,
                        Expression.Constant(input));
                    if (quotedInvocation.Expression != explicitQuote ||
                        quotedInvocation.Type != typeof(int) ||
                        quotedInvocation.Arguments.Count != 1)
                        return 22;
                    var rejectedUnquotedExpressionValue = false;
                    try
                    {
                        _ = Expression.Invoke(
                            Expression.Constant(
                                inner,
                                typeof(Expression<Func<int, int>>)),
                            Expression.Constant(input));
                    }
                    catch (NotSupportedException)
                    {
                        rejectedUnquotedExpressionValue = true;
                    }
                    if (!rejectedUnquotedExpressionValue)
                        return 23;

                    var objectParameter = Expression.Parameter(
                        typeof(object),
                        "objectValue");
                    var implicitBoxingRejections = 0;
                    try
                    {
                        _ = Expression.Assign(
                            objectParameter,
                            Expression.Constant(1));
                    }
                    catch (ArgumentException)
                    {
                        implicitBoxingRejections++;
                    }
                    try
                    {
                        _ = Expression.Lambda<Func<object>>(
                            Expression.Constant(1));
                    }
                    catch (ArgumentException)
                    {
                        implicitBoxingRejections++;
                    }
                    try
                    {
                        _ = Expression.Call(
                            null,
                            ((MethodCallExpression)objectCall.Body).Method,
                            Expression.Constant(1));
                    }
                    catch (ArgumentException)
                    {
                        implicitBoxingRejections++;
                    }
                    try
                    {
                        _ = new InvalidReduction().ReduceAndCheck();
                    }
                    catch (InvalidOperationException)
                    {
                        implicitBoxingRejections++;
                    }
                    if (implicitBoxingRejections != 4)
                        return 12;

                    var unsupportedOperators = 0;
                    try
                    {
                        _ = Expression.Add(
                            Expression.Constant((byte)1),
                            Expression.Constant((byte)2));
                    }
                    catch (NotSupportedException)
                    {
                        unsupportedOperators++;
                    }
                    try
                    {
                        _ = Expression.Negate(Expression.Constant((uint)1));
                    }
                    catch (NotSupportedException)
                    {
                        unsupportedOperators++;
                    }
                    try
                    {
                        _ = Expression.Add(
                            Expression.Constant(1m),
                            Expression.Constant(2m));
                    }
                    catch (NotSupportedException)
                    {
                        unsupportedOperators++;
                    }
                    try
                    {
                        _ = Expression.Convert(
                            Expression.Constant(1m),
                            typeof(int));
                    }
                    catch (NotSupportedException)
                    {
                        unsupportedOperators++;
                    }
                    try
                    {
                        _ = Expression.AndAlso(
                            customOperator.Parameters[0],
                            Expression.Constant(1),
                            userAdd.Method);
                    }
                    catch (NotSupportedException)
                    {
                        unsupportedOperators++;
                    }
                    if (unsupportedOperators != 5)
                        return 13;

                    var openTypeRejections = 0;
                    var openType = typeof(List<>);
                    try
                    {
                        _ = Expression.Parameter(openType);
                    }
                    catch (ArgumentException)
                    {
                        openTypeRejections++;
                    }
                    try
                    {
                        _ = Expression.Constant(null, openType);
                    }
                    catch (ArgumentException)
                    {
                        openTypeRejections++;
                    }
                    try
                    {
                        _ = Expression.Default(openType);
                    }
                    catch (ArgumentException)
                    {
                        openTypeRejections++;
                    }
                    if (openTypeRejections != 3)
                        return 14;

                    var receiver = Expression.Parameter(
                        typeof(object),
                        "receiver");
                    var getter = new FakeMethod(
                        typeof(object),
                        typeof(int));
                    var setter = new FakeMethod(
                        typeof(object),
                        typeof(void),
                        typeof(int));
                    var readOnlyProperty = new FakeProperty(
                        typeof(object),
                        typeof(int),
                        getter,
                        null);
                    var writeOnlyProperty = new FakeProperty(
                        typeof(object),
                        typeof(int),
                        null,
                        setter);
                    var indexedProperty = new FakeProperty(
                        typeof(object),
                        typeof(int),
                        new FakeMethod(
                            typeof(object),
                            typeof(int),
                            typeof(int)),
                        null);
                    var readOnlyMember = Expression.Property(
                        receiver,
                        readOnlyProperty);
                    var writeOnlyMember = Expression.Property(
                        receiver,
                        writeOnlyProperty);
                    var valueReceiverCall = Expression.Call(
                        Expression.Constant(1),
                        new FakeMethod(typeof(object), typeof(int)));
                    var propertyValidation = 0;
                    if (valueReceiverCall.Object is ConstantExpression)
                    {
                        propertyValidation++;
                    }
                    try
                    {
                        _ = Expression.Call(
                            receiver,
                            new FakeMethod(
                                typeof(object),
                                typeof(int),
                                typeof(Expression)),
                            inner);
                    }
                    catch (ArgumentException)
                    {
                        propertyValidation++;
                    }
                    try
                    {
                        _ = Expression.Assign(
                            readOnlyMember,
                            Expression.Constant(1));
                    }
                    catch (ArgumentException)
                    {
                        propertyValidation++;
                    }
                    if (Expression.Assign(
                            writeOnlyMember,
                            Expression.Constant(1)).Left == writeOnlyMember)
                    {
                        propertyValidation++;
                    }
                    try
                    {
                        _ = Expression.Add(
                            writeOnlyMember,
                            Expression.Constant(1));
                    }
                    catch (ArgumentException)
                    {
                        propertyValidation++;
                    }
                    try
                    {
                        _ = Expression.Property(receiver, indexedProperty);
                    }
                    catch (ArgumentException)
                    {
                        propertyValidation++;
                    }
                    if (propertyValidation != 6)
                        return 15;

                    var blockFailures = 0;
                    try
                    {
                        _ = Expression.Block((Expression[])null!);
                    }
                    catch (ArgumentNullException)
                    {
                        blockFailures++;
                    }
                    try
                    {
                        _ = Expression.Block(new Expression[0]).Result;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        blockFailures++;
                    }
                    if (blockFailures != 2)
                        return 16;

                    if (inner.Update(
                            inner.Body,
                            new SinglePass<ParameterExpression>(value)) != inner ||
                        block.Update(
                            new SinglePass<ParameterExpression>(local),
                            new SinglePass<Expression>(assign, invoke)) != block ||
                        invoke.Update(
                            invoke.Expression,
                            new SinglePass<Expression>(local)) != invoke ||
                        quotedCall.Update(
                            quotedCall.Object,
                            new SinglePass<Expression>(
                                quotedCall.Arguments[0],
                                quotedCall.Arguments[1])) != quotedCall ||
                        ((NewExpression)constructor.Body).Update(
                            new SinglePass<Expression>(
                                constructor.Parameters[0])) != constructor.Body)
                        return 17;

                    return 42;
                }

                private static int Accept(
                    Expression<Func<int, int>> expression,
                    int value) => value;

                private static object Identity(object value) => value;

                private sealed class InvalidReduction : Expression
                {
                    public override ExpressionType NodeType =>
                        ExpressionType.Extension;

                    public override Type Type => typeof(object);

                    public override bool CanReduce => true;

                    public override Expression Reduce() => Expression.Constant(1);
                }

                private sealed class SinglePass<T> : IEnumerable<T>
                {
                    private readonly T[] _items;
                    private bool _used;

                    internal SinglePass(params T[] items)
                    {
                        _items = items;
                    }

                    public IEnumerator<T> GetEnumerator()
                    {
                        if (_used)
                        {
                            throw new InvalidOperationException(
                                "The sequence was enumerated more than once.");
                        }
                        _used = true;
                        return ((IEnumerable<T>)_items).GetEnumerator();
                    }

                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }

                private sealed class FakeProperty : PropertyInfo
                {
                    private readonly Type _declaringType;
                    private readonly Type _propertyType;
                    private readonly MethodInfo? _getter;
                    private readonly MethodInfo? _setter;

                    internal FakeProperty(
                        Type declaringType,
                        Type propertyType,
                        MethodInfo? getter,
                        MethodInfo? setter)
                    {
                        _declaringType = declaringType;
                        _propertyType = propertyType;
                        _getter = getter;
                        _setter = setter;
                    }

                    public override Type DeclaringType => _declaringType;

                    public override Type PropertyType => _propertyType;

                    public override MethodInfo? GetGetMethod(bool nonPublic) =>
                        _getter;

                    public override MethodInfo? GetSetMethod(bool nonPublic) =>
                        _setter;
                }

                private sealed class FakeMethod : MethodInfo
                {
                    private readonly Type _declaringType;
                    private readonly Type _returnType;
                    private readonly ParameterInfo[] _parameters;

                    internal FakeMethod(
                        Type declaringType,
                        Type returnType,
                        params Type[] parameterTypes)
                    {
                        _declaringType = declaringType;
                        _returnType = returnType;
                        _parameters = new ParameterInfo[parameterTypes.Length];
                        for (var index = 0; index < parameterTypes.Length; index++)
                        {
                            _parameters[index] = new FakeParameter(
                                parameterTypes[index]);
                        }
                    }

                    public override Type DeclaringType => _declaringType;

                    public override Type ReturnType => _returnType;

                    public override bool IsStatic => false;

                    public override ParameterInfo[] GetParameters() => _parameters;
                }

                private sealed class FakeParameter : ParameterInfo
                {
                    private readonly Type _type;

                    internal FakeParameter(Type type)
                    {
                        _type = type;
                    }

                    public override Type ParameterType => _type;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTreeConstructionFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
        var lambdaExpressionInvoke = Assert.Single(
            compilation.Program.DelegateInvokeDescriptors.Values,
            candidate => candidate.DeclaringType.Shape ==
                    CliTypeShape.GenericInstantiation &&
                candidate.DeclaringType.ElementType!.FullName == "System.Func`1" &&
                candidate.DeclaringType.TypeArguments.Length == 1 &&
                candidate.DeclaringType.TypeArguments[0].FullName ==
                    "System.Linq.Expressions.LambdaExpression");
        Assert.Equal(
            "System.Linq.Expressions.LambdaExpression",
            lambdaExpressionInvoke.Signature.ReturnSignatureType.FullName);
        Assert.DoesNotContain(
            compilation.Program.Methods.Values,
            method => method.Method.Definition.Name is
                "op_Addition" or "op_Explicit" or "Accept" &&
                method.Method.Definition.Key.Assembly.Name ==
                    "ExpressionTreeConstructionFixture");
        Assert.DoesNotContain(
            compilation.Program.ConstructedMethods.Values,
            method => method.Method.Definition.Name is
                "op_Addition" or "op_Explicit" or "Accept" &&
                method.Method.Definition.Key.Assembly.Name ==
                    "ExpressionTreeConstructionFixture");
        Assert.Empty(compilation.Program.NamedMemberDescriptors);
        Assert.Equal(
            RuntimeTypeNamePayload.None,
            compilation.Program.TypeNamePayload);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void VisitorsRewriteAndFormattingRootsOnlyDemandedNames(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTreeVisitorFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionTreeVisitorFixture;

            public sealed class Model
            {
                public int Field;
                public static int StaticField;

                public Model(int value)
                {
                    Field = value;
                }

                public int Property => Field;

                public int Add(int value) => Field + value;

                public static Model operator +(Model value, int increment) =>
                    new(value.Field + increment);

                public static explicit operator int(Model value) => value.Field;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<int, int>> arithmetic = value => value + 1;
                    Expression<Func<int, int>> conditional =
                        value => value > 0 && value < 10 ? value : -value;
                    Expression<Func<Model, int>> property = model => model.Property;
                    Expression<Func<Model, int>> field = model => model.Field;
                    Expression<Func<int>> staticField = () => Model.StaticField;
                    Expression<Func<int, Model>> created = value => new Model(value);
                    Expression<Func<Model, int>> call = model => model.Add(1);
                    Expression<Func<Model, Model>> customOperator = model => model + 1;
                    Expression<Func<Model, int>> conversion = model => (int)model;
                    Expression<Func<int, int>> quote =
                        value => Accept(inner => inner + 1, value);

                    var local = Expression.Variable(typeof(int), "local");
                    var assignment = Expression.Assign(
                        local,
                        Expression.Constant(41));
                    var invocation = Expression.Invoke(arithmetic, local);
                    var block = Expression.Block(
                        new[] { local },
                        assignment,
                        invocation);
                    var second = Expression.Variable(typeof(int), "second");
                    var multiVariableBlock = Expression.Block(
                        new[] { local, second },
                        assignment);
                    var unnamed = Expression.Parameter(typeof(int));
                    var emptyName = Expression.Parameter(typeof(int), "");
                    var marker = new Marker();

                    if (marker.ToString() != marker.GetType().ToString())
                        return 80;

                    if (arithmetic.ToString() != "value => (value + 1)" ||
                        conditional.ToString() !=
                            "value => IIF(((value > 0) AndAlso (value < 10)), value, -value)")
                        return 11;
                    if (property.ToString() != "model => model.Property" ||
                        field.ToString() != "model => model.Field")
                        return 12;
                    var staticFieldText = staticField.ToString();
                    var staticMember = (MemberExpression)staticField.Body;
                    if (staticMember.Member.Name != "StaticField")
                        return 14;
                    if (staticMember.Member.DeclaringType!.Name != "Model")
                        return 15;
                    if (staticMember.Expression is not null)
                        return 16;
                    if (staticFieldText != "() => Model.StaticField")
                    {
                        if (staticFieldText == "() => StaticField")
                            return 17;
                        if (staticFieldText == "Model.StaticField")
                            return 18;
                        return 100 + staticFieldText.Length;
                    }
                    if (
                        created.ToString() != "value => new Model(value)" ||
                        call.ToString() != "model => model.Add(1)" ||
                        customOperator.ToString() != "model => (model + 1)" ||
                        conversion.ToString() != "model => Convert(model, Int32)" ||
                        quote.ToString() !=
                            "value => Accept(inner => (inner + 1), value)")
                        return 6;
                    if (
                        block.ToString() != "{var local; ... }" ||
                        multiVariableBlock.ToString() !=
                            "{var local;var second; ... }" ||
                        Expression.Block(new Expression[0]).ToString() !=
                            "{ ... }" ||
                        assignment.ToString() != "(local = 41)" ||
                        invocation.ToString() !=
                            "Invoke(value => (value + 1), local)" ||
                        Expression.Empty().ToString() != "default(Void)")
                        return 7;
                    if (Expression.Constant(null, typeof(string)).ToString() != "null")
                        return 81;
                    if (Expression.Constant("hello").ToString() != "\"hello\"")
                        return 82;
                    if (Expression.Constant('x').ToString() != "x")
                        return 83;
                    if (unnamed.ToString() != "Param_0")
                        return 84;
                    if (emptyName.ToString() != "Param_0")
                        return 85;
                    if (Expression.Constant(marker).ToString() !=
                        "value(" + marker.GetType().ToString() + ")")
                        return 86;

                    var identity = new IdentityVisitor();
                    if (identity.Visit(arithmetic) != arithmetic ||
                        identity.Visit(conditional) != conditional ||
                        identity.Visit(property) != property ||
                        identity.Visit(created) != created ||
                        identity.Visit(call) != call ||
                        identity.Visit(block) != block)
                        return 2;

                    var rewritten = (Expression<Func<int, int>>)new IncrementVisitor()
                        .Visit(arithmetic)!;
                    var rewrittenAdd = (BinaryExpression)rewritten.Body;
                    if (rewritten == arithmetic ||
                        rewritten.Parameters[0] != arithmetic.Parameters[0] ||
                        rewrittenAdd.Right is not ConstantExpression
                            { Value: int replacement } ||
                        replacement != 2 ||
                        ((ConstantExpression)((BinaryExpression)arithmetic.Body).Right)
                            .Value is not int original || original != 1)
                        return 3;

                    var rejectedWrongType = false;
                    try
                    {
                        _ = new WrongParameterVisitor().VisitAndConvert(
                            arithmetic.Parameters[0],
                            "Run");
                    }
                    catch (InvalidOperationException)
                    {
                        rejectedWrongType = true;
                    }
                    if (!rejectedWrongType)
                        return 4;

                    var rejectedChildType = false;
                    try
                    {
                        _ = new LongConstantVisitor().Visit(Expression.Add(
                            Expression.Constant(1),
                            Expression.Constant(1)));
                    }
                    catch (InvalidOperationException)
                    {
                        rejectedChildType = true;
                    }
                    if (!rejectedChildType)
                        return 4;

                    var rejectedNullChildren = 0;
                    try
                    {
                        _ = new NullConstantVisitor().Visit(Expression.Add(
                            Expression.Constant(1),
                            Expression.Constant(1)));
                    }
                    catch (ArgumentNullException)
                    {
                        rejectedNullChildren++;
                    }
                    try
                    {
                        _ = new NullConstantVisitor().Visit(
                            Expression.Negate(Expression.Constant(1)));
                    }
                    catch (ArgumentNullException)
                    {
                        rejectedNullChildren++;
                    }
                    if (rejectedNullChildren != 2)
                        return 4;

                    var reduced = identity.Visit(
                        new ReducibleExpression(Expression.Constant(input)));
                    if (reduced is not ConstantExpression { Value: int value } ||
                        value != input)
                        return 5;

                    return 42;
                }

                private static int Accept(
                    Expression<Func<int, int>> expression,
                    int value) => value;

                private sealed class IdentityVisitor : ExpressionVisitor
                {
                }

                private sealed class IncrementVisitor : ExpressionVisitor
                {
                    protected override Expression VisitConstant(
                        ConstantExpression node) =>
                        node.Value is int value && value == 1
                            ? Expression.Constant(2)
                            : node;
                }

                private sealed class WrongParameterVisitor : ExpressionVisitor
                {
                    protected override Expression VisitParameter(
                        ParameterExpression node) => Expression.Constant(0);
                }

                private sealed class LongConstantVisitor : ExpressionVisitor
                {
                    protected override Expression VisitConstant(
                        ConstantExpression node) =>
                        node.Value is int value && value == 1
                            ? Expression.Constant(1L)
                            : node;
                }

                private sealed class NullConstantVisitor : ExpressionVisitor
                {
                    protected override Expression VisitConstant(
                        ConstantExpression node) => null!;
                }

                private sealed class Marker
                {
                }

                private sealed class ReducibleExpression : Expression
                {
                    private readonly Expression _reduced;

                    internal ReducibleExpression(Expression reduced)
                    {
                        _reduced = reduced;
                    }

                    public override ExpressionType NodeType =>
                        ExpressionType.Extension;

                    public override Type Type => _reduced.Type;

                    public override bool CanReduce => true;

                    public override Expression Reduce() => _reduced;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTreeVisitorFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
        Assert.NotEmpty(compilation.Program.NamedMemberDescriptors);
        Assert.NotEqual(
            RuntimeTypeNamePayload.None,
            compilation.Program.TypeNamePayload & RuntimeTypeNamePayload.Name);
        Assert.DoesNotContain(
            compilation.Program.Methods.Values,
            method => method.Method.Definition.Name is
                "get_Property" or "Add" or "op_Addition" or "op_Explicit" &&
                method.Method.Definition.Key.Assembly.Name ==
                    "ExpressionTreeVisitorFixture");
    }
}
