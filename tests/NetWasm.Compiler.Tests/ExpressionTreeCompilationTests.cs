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
    public void EnumValuesFlowThroughPropertyAccessorsAndDefaults(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Linq.Expressions;
            namespace EnumExpressionValues;
            [Flags] public enum Access : long { None = 0, Read = 1, High = 1L << 40 }
            public sealed class Model { public Access Value { get; set; } }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<Model, Access>> property = model => model.Value;
                    var get = property.Compile();
                    if (get(new Model { Value = Access.Read | Access.High }) != (Access.Read | Access.High)) return 1;
                    Expression<Func<Access, Access>> identity = value => value;
                    if (identity.Compile()(Access.High) != Access.High) return 2;
                    var unused = Expression.Parameter(typeof(int), "unused");
                    var defaultValue = Expression.Lambda<Func<int, Access>>(
                        Expression.Default(typeof(Access)), unused).Compile();
                    if (defaultValue(input) != Access.None) return 3;
                    return 42;
                }
            }
            """;
        var assembly = optimized ? assets.CompileOptimizedSource("EnumExpressionValues", source) :
            assets.CompileSource("EnumExpressionValues", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "EnumExpressionValues.EntryPoint", "Run", [], Target: target));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 41, target, compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void EqualityExpressionsExecuteWithDesktopSemantics(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionEqualityFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionEqualityFixture;

            public sealed class Model
            {
                public Model(string name, int identity)
                {
                    Name = name;
                    Identity = identity;
                }

                public string Name { get; }

                public int Identity { get; }

                public static bool operator ==(Model? left, Model? right) =>
                    ReferenceEquals(left, right) ||
                    left is not null && right is not null &&
                    left.Identity == right.Identity;

                public static bool operator !=(Model? left, Model? right) =>
                    !(left == right);

                public override bool Equals(object? value) =>
                    value is Model other && this == other;

                public override int GetHashCode() => Identity;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<Model, bool>> stringEquality =
                        model => model.Name == "Ada";
                    var stringPredicate = stringEquality.Compile();
                    if (!stringPredicate(new Model("Ada", input)) ||
                        stringPredicate(new Model("Grace", input)))
                        return 1;

                    Expression<Func<int, bool>> primitiveEquality =
                        value => value == input;
                    var primitivePredicate = primitiveEquality.Compile();
                    if (!primitivePredicate(input) || primitivePredicate(input + 1))
                        return 2;

                    object same = new Model("same", input);
                    Expression<Func<object, bool>> referenceEquality =
                        value => value == same;
                    var referencePredicate = referenceEquality.Compile();
                    if (!referencePredicate(same) ||
                        referencePredicate(new Model("same", input)))
                        return 3;

                    var equivalent = new Model("equivalent", input);
                    Expression<Func<Model, bool>> overloadedEquality =
                        value => value == equivalent;
                    var overloadedPredicate = overloadedEquality.Compile();
                    if (!overloadedPredicate(new Model("other", input)) ||
                        overloadedPredicate(new Model("other", input + 1)))
                        return 4;

                    var value = Expression.Parameter(typeof(int), "value");
                    var programmatic = Expression.Lambda<Func<int, bool>>(
                        Expression.Equal(value, Expression.Constant(input)),
                        value).Compile(preferInterpretation: true);
                    if (!programmatic(input) || programmatic(input + 1))
                        return 5;

                    return 42;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionEqualityFixture.EntryPoint",
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

                    // Lambdas of any arity run: a captured value with no parameters, and the first of two.
                    Expression<Func<int>> zero = () => input;
                    if (zero.Parameters.Count != 0) return 24;
                    if (zero.Compile()() != input) return 25;
                    if (((Func<int>)((LambdaExpression)zero).Compile())() != input) return 26;

                    Expression<Func<Model, Model, int>> binary =
                        (left, right) => left.Field;
                    var leftModel = new Model(input + 2, "left");
                    var rightModel = new Model(input + 3, "right");
                    if (binary.Compile()(leftModel, rightModel) != input + 2) return 27;
                    if (((Func<Model, Model, int>)((LambdaExpression)binary).Compile(true))(rightModel, leftModel) != input + 3) return 28;

                    // A void call runs, through the typed and the untyped Compile.
                    Expression<Action<Model>> voidTree = model => model.Touch();
                    var touched = new Model(input, "touched");
                    voidTree.Compile()(touched);
                    if (touched.Field != input + 1) return 29;
                    ((Action<Model>)((LambdaExpression)voidTree).Compile())(touched);
                    if (touched.Field != input + 2) return 30;

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
            TestAssets.RepositorySdkVersion,
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

    // Any value type as a value: structs, DateTime and their nullable forms as parameters, results, constants, locals,
    // array elements, Default and New(Type), and through boxing, interface and nullable conversions, with desktop's
    // exceptions for a bad unboxing.
    // Interpreter stage 4: assignment to fields and properties (of objects, of structs in their boxes, static),
    // an assignment's value, member initializers with nested and collection bindings, and collection initializers.
    // A struct initializer run twice starts from a fresh zero value each time.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledMemberAssignmentsAndInitializersRunAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionAssignFixture",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq.Expressions;
            using System.Reflection;

            namespace ExpressionAssignFixture;

            public struct Point
            {
                public int X;
                public int Y { get; set; }
                public string? Label;
            }

            // C# builds each parameterless class construction below as New(typeof(C)); the compiler gives it C's
            // constructor.
            public sealed class Settings
            {
                public int Count;
                public string Name { get; set; } = "";
                public Point Origin;
                public List<int> Values { get; } = new();
                public static int Shared;
            }

            public sealed class Holder
            {
                public Settings Inner { get; } = new();
            }

            public sealed class Box<T>
            {
                public T? Value;
            }

            public static class EntryPoint
            {
                // Members as C# expression trees carry them (by token), not looked up by name.
                static FieldInfo FieldOf<T, TValue>(Expression<Func<T, TValue>> access) =>
                    (FieldInfo)((MemberExpression)access.Body).Member;

                static PropertyInfo PropertyOf<T, TValue>(Expression<Func<T, TValue>> access) =>
                    (PropertyInfo)((MemberExpression)access.Body).Member;

                public static int Run(int input)
                {
                    // Member initializers as C# builds them: assignments, a struct's, and a collection binding. They
                    // also bring the setters' descriptors that the assignments below use.
                    Expression<Func<int, Settings>> init = n => new Settings
                    {
                        Count = n,
                        Name = "init",
                        Origin = new Point { X = n, Y = 2, Label = "o" },
                        Values = { n, n + 1 },
                    };
                    var initialized = init.Compile()(input);
                    if (initialized.Count != input || initialized.Name != "init" || initialized.Origin.Y != 2 ||
                        initialized.Origin.Label != "o" || initialized.Values.Count != 2 ||
                        initialized.Values[1] != input + 1) return 5;

                    var count = FieldOf((Settings s) => s.Count);
                    var name = PropertyOf((Settings s) => s.Name);
                    var origin = FieldOf((Settings s) => s.Origin);
                    var x = FieldOf((Point p) => p.X);
                    var y = PropertyOf((Point p) => p.Y);
                    var label = FieldOf((Point p) => p.Label);
                    var shared = (FieldInfo)((MemberExpression)((Expression<Func<int>>)(() => Settings.Shared)).Body).Member;
                    var settings = Expression.Parameter(typeof(Settings), "settings");
                    var value = Expression.Parameter(typeof(int), "value");

                    // Field and property assignment, as statements.
                    var assign = Expression.Lambda<Action<Settings, int>>(
                        Expression.Block(
                            Expression.Assign(Expression.Field(settings, count), value),
                            Expression.Assign(Expression.Property(settings, name), Expression.Constant("named")),
                            Expression.Assign(Expression.Field(settings, origin),
                                Expression.MemberInit(Expression.New(typeof(Point)), Expression.Bind(x, value)))),
                        settings, value).Compile();
                    var target = new Settings();
                    assign(target, input);
                    if (target.Count != input || target.Name != "named" || target.Origin.X != input) return 1;

                    // An assignment's value is the assigned value.
                    var chained = Expression.Lambda<Func<Settings, int, int>>(
                        Expression.Add(Expression.Assign(Expression.Field(settings, count), value), Expression.Constant(1)),
                        settings, value).Compile();
                    if (chained(target, 7) != 8 || target.Count != 7) return 2;

                    // A struct's field and property, changed in the value the lambda holds.
                    var point = Expression.Variable(typeof(Point), "point");
                    var structs = Expression.Lambda<Func<int, Point>>(
                        Expression.Block(new[] { point },
                            Expression.Assign(Expression.Field(point, x), value),
                            Expression.Assign(Expression.Property(point, y), Expression.Add(value, Expression.Constant(1))),
                            Expression.Assign(Expression.Field(point, label), Expression.Constant("p")),
                            point),
                        value).Compile();
                    var made = structs(input);
                    if (made.X != input || made.Y != input + 1 || made.Label != "p") return 3;

                    // A static field.
                    var statics = Expression.Lambda<Action<int>>(
                        Expression.Assign(Expression.Field(null, shared), value), value).Compile();
                    statics(input);
                    if (Settings.Shared != input) return 4;

                    // A nested binding on a member that holds an object.
                    Expression<Func<int, Holder>> nested = n => new Holder { Inner = { Count = n, Values = { n } } };
                    var holder = nested.Compile()(input);
                    if (holder.Inner.Count != input || holder.Inner.Values[0] != input) return 6;

                    // A struct initializer starts from zero each run.
                    Expression<Func<int, Point>> fresh = n => new Point { Y = n };
                    var freshPoint = fresh.Compile();
                    if (freshPoint(3).X != 0 || freshPoint(4).Y != 4) return 7;

                    // Collection initializers, with one and two arguments.
                    Expression<Func<int, List<int>>> list = n => new List<int> { n, n * 2, n * 3 };
                    var built = list.Compile()(input);
                    if (built.Count != 3 || built[2] != input * 3) return 8;
                    Expression<Func<int, Dictionary<string, int>>> map = n => new Dictionary<string, int> { { "a", n }, { "b", n + 1 } };
                    var dictionary = map.Compile()(input);
                    if (dictionary.Count != 2 || dictionary["b"] != input + 1) return 9;

                    // A generic class's parameterless construction, and a bare one.
                    Expression<Func<string, Box<string>>> box = text => new Box<string> { Value = text };
                    if (box.Compile()("boxed").Value != "boxed") return 10;
                    Expression<Func<Settings>> bare = () => new Settings();
                    if (bare.Compile()().Count != 0) return 11;

                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionAssignFixture.EntryPoint",
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

    // Interpreter stage 3: members of structs (run on the box's contents), static fields and properties, void
    // calls, struct arguments and results, and constructors of classes and structs. Two callees collect and then
    // allocate objects the size of the result while the new object or the struct result's box is held only as the
    // call's allocation root; a box left unrooted would be reused, and the kept objects would show it.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledStructMembersStaticsVoidCallsAndConstructorsRunAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionMemberFixture",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq.Expressions;

            namespace ExpressionMemberFixture;

            public struct Point
            {
                public Point(int x, int y) { X = x; Y = y; Tag = "p"; }
                public int X;
                public int Y;
                public string Tag;
                public int Sum => X + Y;
                public Point Add(Point other) => new Point(X + other.X, Y + other.Y);
                public override string ToString() => X + "," + Y;
            }

            public sealed class Shape
            {
                public Shape(Point origin, string name) { Origin = origin; Name = name; }
                public Point Origin;
                public string Name { get; }
            }

            public sealed class Log
            {
                public int Total;
                public void Write(int value) => Total += value;
            }

            public static class Holder
            {
                public static int Count;
                public static Point Corner = new Point(7, 9);
                public static string Label => "label" + Count;
                static Holder() { Count = 5; }
            }

            public static class Pressure
            {
                public static readonly List<object> Kept = new();

                public static Point Make(int n)
                {
                    GC.Collect();
                    for (var i = 0; i < 2000; i++) Kept.Add(new Point(i, -i));
                    return new Point(n, n * 2);
                }

                public static bool KeptIntact()
                {
                    foreach (var kept in Kept)
                    {
                        if (kept is not Point p || p.X != -p.Y || p.Tag != "p") return false;
                    }
                    return Kept.Count == 2000;
                }
            }

            public sealed class Built
            {
                public static readonly List<Built> Kept = new();
                public readonly string Text;

                public Built(int n)
                {
                    GC.Collect();
                    for (var i = 0; i < 2000; i++) Kept.Add(new Built("k" + i));
                    Text = "built" + n;
                }

                private Built(string text) => Text = text;

                public static bool KeptIntact()
                {
                    for (var i = 0; i < Kept.Count; i++)
                    {
                        if (Kept[i].Text != "k" + i) return false;
                    }
                    return Kept.Count == 2000;
                }
            }

            public static class EntryPoint
            {
                static bool Throws<T>(Action run) where T : Exception
                {
                    try { run(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    var p = new Point(input, 2);
                    Expression<Func<Point, int>> sum = v => v.Sum;
                    if (sum.Compile()(p) != input + 2) return 1;
                    Expression<Func<Point, int>> x = v => v.X;
                    if (x.Compile()(p) != input) return 2;
                    Expression<Func<Point, Point, Point>> add = (a, b) => a.Add(b);
                    var added = add.Compile()(p, new Point(1, 1));
                    if (added.X != input + 1 || added.Y != 3 || added.Tag != "p") return 3;
                    Expression<Func<Point, string>> text = v => v.ToString();
                    if (text.Compile()(p) != input + ",2") return 4;

                    // Statics: a field behind a type initializer, a struct field and a property.
                    Expression<Func<int>> count = () => Holder.Count;
                    if (count.Compile()() != 5) return 5;
                    Expression<Func<Point>> corner = () => Holder.Corner;
                    if (corner.Compile()().Y != 9) return 6;
                    Expression<Func<string>> label = () => Holder.Label;
                    if (label.Compile()() != "label5") return 7;

                    // A struct-valued field of a class, and a field of that struct.
                    var shape = new Shape(p, "s");
                    Expression<Func<Shape, Point>> origin = s => s.Origin;
                    var originOf = origin.Compile();
                    if (originOf(shape).X != input || originOf(shape).Tag != "p") return 8;
                    Expression<Func<Shape, int>> nested = s => s.Origin.Y;
                    if (nested.Compile()(shape) != 2) return 9;

                    // A void call.
                    var log = new Log();
                    Expression<Action<Log, int>> write = (l, v) => l.Write(v);
                    var writer = write.Compile();
                    writer(log, 3);
                    writer(log, 4);
                    if (log.Total != 7) return 10;

                    // Constructors of a struct and of a class.
                    Expression<Func<int, int, Point>> make = (a, b) => new Point(a, b);
                    var made = make.Compile()(4, 5);
                    if (made.X != 4 || made.Y != 5 || made.Tag != "p") return 11;
                    Expression<Func<Point, string, Shape>> build = (o, n) => new Shape(o, n);
                    var built = build.Compile()(p, "named");
                    if (built.Name != "named" || built.Origin.X != input) return 12;

                    // Collections while the result is held only as the call's allocation root.
                    Expression<Func<int, Point>> pressure = n => Pressure.Make(n);
                    var pressured = pressure.Compile()(input);
                    if (pressured.X != input || pressured.Y != input * 2 || pressured.Tag != "p" ||
                        !Pressure.KeptIntact()) return 13;
                    Expression<Func<int, Built>> collected = n => new Built(n);
                    var collectedObject = collected.Compile()(input);
                    if (collectedObject.Text != "built" + input || !Built.KeptIntact()) return 14;

                    if (!Throws<NullReferenceException>(() => originOf(null!))) return 15;
                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionMemberFixture.EntryPoint",
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
    public void CompiledNestedLambdasQuotesAndInvocationsRunAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionClosureFixture",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;
            using System.Reflection;

            namespace ExpressionClosureFixture;

            public static class Helpers
            {
                public static int CallTwice(Func<int> function)
                {
                    function();
                    return function();
                }

                public static int SumAll(List<Func<int>> functions)
                {
                    var sum = 0;
                    foreach (var function in functions) sum += function();
                    return sum;
                }

                public static int MessageLength(Func<Exception> function) => function().Message.Length;
            }

            public static class EntryPoint
            {
                private static MethodInfo MethodOf<T>(Expression<Func<T>> call) => ((MethodCallExpression)call.Body).Method;

                private static MethodInfo ActionOf(Expression<Action> call) => ((MethodCallExpression)call.Body).Method;

                public static int Run(int input)
                {
                    // A nested lambda captures the outer parameter.
                    Expression<Func<int, Func<int, int>>> adder = x => y => x + y;
                    if (adder.Compile()(input)(1) != input + 1) return 1;

                    // LINQ to objects with a lambda that captures.
                    Expression<Func<int[], int, bool>> any = (items, n) => items.Any(i => i > n);
                    var anyCompiled = any.Compile();
                    if (!anyCompiled(new[] { 1, 2, input }, 40) || anyCompiled(new[] { 1, 2 }, 40)) return 2;
                    Expression<Func<List<int>, int, int>> sum = (list, k) => list.Where(i => i % k == 0).Select(i => i * k).Sum();
                    if (sum.Compile()(new List<int> { 1, 2, 3, 4, 5, 6 }, 2) != 24) return 3;

                    // A closure changes the variable it shares with its block.
                    var counter = Expression.Variable(typeof(int), "counter");
                    var mutate = Expression.Lambda<Func<int>>(Expression.Block(new[] { counter },
                        Expression.Assign(counter, Expression.Constant(input)),
                        Expression.Call(MethodOf(() => Helpers.CallTwice(null!)),
                            Expression.Lambda<Func<int>>(Expression.PreIncrementAssign(counter))),
                        counter));
                    if (mutate.Compile()() != input + 2) return 4;

                    // Each entry to a block in a loop has its own variable.
                    var index = Expression.Variable(typeof(int), "index");
                    var value = Expression.Variable(typeof(int), "value");
                    var functions = Expression.Variable(typeof(List<Func<int>>), "functions");
                    var done = Expression.Label("done");
                    var loop = Expression.Lambda<Func<int>>(Expression.Block(new[] { index, functions },
                        Expression.Assign(functions, Expression.New(typeof(List<Func<int>>))),
                        Expression.Loop(
                            Expression.IfThenElse(
                                Expression.LessThan(index, Expression.Constant(3)),
                                Expression.Block(new[] { value },
                                    Expression.Assign(value, index),
                                    Expression.Call(functions, ActionOf(() => new List<Func<int>>().Add(null!)),
                                        Expression.Lambda<Func<int>>(value)),
                                    Expression.PostIncrementAssign(index)),
                                Expression.Break(done)),
                            done),
                        Expression.Call(MethodOf(() => Helpers.SumAll(null!)), functions)));
                    if (loop.Compile()() != 3) return 5;

                    // C# quotes an inner lambda whose type is an expression; the quote reads what it captures.
                    Expression<Func<int, Expression<Func<int, int>>>> quoted = x => y => x + y;
                    if (quoted.Compile()(input).Compile()(1) != input + 1) return 6;
                    Expression<Func<int, Expression<Func<int, int>>>> plain = x => y => y * 2;
                    if (plain.Compile()(input).Compile()(input) != input * 2) return 7;

                    // Invoke of a lambda, which reads the outer parameter.
                    var p = Expression.Parameter(typeof(int), "p");
                    var q = Expression.Parameter(typeof(int), "q");
                    var invoke = Expression.Lambda<Func<int, int>>(
                        Expression.Invoke(
                            Expression.Lambda<Func<int, int>>(Expression.Add(q, p), q),
                            Expression.Multiply(p, Expression.Constant(2))),
                        p);
                    if (invoke.Compile()(input) != input * 3) return 8;

                    // A captured catch variable, then a rethrow of what the handler took.
                    var exception = Expression.Variable(typeof(Exception), "exception");
                    var caught = Expression.Variable(typeof(int), "caught");
                    var handler = Expression.Lambda<Func<int>>(Expression.Block(new[] { caught },
                        Expression.TryCatch(
                            Expression.Block(typeof(void),
                                Expression.TryCatch(
                                    Expression.Throw(Expression.Constant(new InvalidOperationException("boom"))),
                                    Expression.Catch(exception, Expression.Block(typeof(void),
                                        Expression.Assign(caught, Expression.Call(
                                            MethodOf(() => Helpers.MessageLength(null!)),
                                            Expression.Lambda<Func<Exception>>(exception))),
                                        Expression.Rethrow())))),
                            Expression.Catch(typeof(InvalidOperationException), Expression.Empty())),
                        caught));
                    if (handler.Compile()() != 4) return 9;

                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionClosureFixture.EntryPoint",
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
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledLiftedOperatorsAndPowerRunAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionLiftedFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionLiftedFixture;

            public readonly struct Money
            {
                public Money(int cents) { Cents = cents; }
                public int Cents { get; }
                public static Money operator +(Money left, Money right) => new(left.Cents + right.Cents);
                public static Money operator -(Money value) => new(-value.Cents);
                public static bool operator <(Money left, Money right) => left.Cents < right.Cents;
                public static bool operator >(Money left, Money right) => left.Cents > right.Cents;
                public static bool operator ==(Money left, Money right) => left.Cents == right.Cents;
                public static bool operator !=(Money left, Money right) => left.Cents != right.Cents;
                public static implicit operator int(Money value) => value.Cents;
                public override bool Equals(object? other) => other is Money money && money.Cents == Cents;
                public override int GetHashCode() => Cents;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    // C# lifts a struct's operators over its nullable form: null in, null (or false) out.
                    Expression<Func<Money?, Money?, Money?>> add = (left, right) => left + right;
                    var addCompiled = add.Compile();
                    if (addCompiled(new Money(input), new Money(1))?.Cents != input + 1) return 1;
                    if (addCompiled(null, new Money(1)) is not null) return 2;
                    Expression<Func<Money?, Money?>> negate = value => -value;
                    var negateCompiled = negate.Compile();
                    if (negateCompiled(new Money(input))?.Cents != -input || negateCompiled(null) is not null) return 3;
                    Expression<Func<Money?, Money?, bool>> less = (left, right) => left < right;
                    var lessCompiled = less.Compile();
                    if (!lessCompiled(new Money(1), new Money(input)) || lessCompiled(null, new Money(1)) ||
                        lessCompiled(new Money(1), null)) return 4;
                    Expression<Func<Money?, Money?, bool>> equal = (left, right) => left == right;
                    var equalCompiled = equal.Compile();
                    if (!equalCompiled(null, null) || equalCompiled(null, new Money(1)) ||
                        !equalCompiled(new Money(2), new Money(2))) return 5;
                    Expression<Func<Money?, Money?, bool>> unequal = (left, right) => left != right;
                    var unequalCompiled = unequal.Compile();
                    if (unequalCompiled(null, null) || !unequalCompiled(null, new Money(1)) ||
                        unequalCompiled(new Money(2), new Money(2))) return 6;
                    Expression<Func<Money?, int?>> convert = value => value;
                    var convertCompiled = convert.Compile();
                    if (convertCompiled(new Money(input)) != input || convertCompiled(null) is not null) return 7;

                    // Power carries Math.Pow.
                    var x = Expression.Parameter(typeof(double), "x");
                    var power = Expression.Lambda<Func<double, double>>(Expression.Power(x, Expression.Constant(3.0)), x);
                    if (power.Compile()(2.0) != 8.0) return 8;

                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionLiftedFixture.EntryPoint",
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
    public void CompiledStructValuesRunAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionStructFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionStructFixture;

            public interface IShape { int Area(); }

            public readonly struct Point : IShape
            {
                public Point(int x, int y) { X = x; Y = y; }
                public int X { get; }
                public int Y { get; }
                public int Area() => X * Y;
                public bool Same(Point other) => X == other.X && Y == other.Y;
            }

            public struct Wide
            {
                public long A;
                public double B;
                public decimal C;
                public string? D;
            }

            public static class EntryPoint
            {
                static bool Throws<T>(Action run) where T : Exception
                {
                    try { run(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    var p = new Point(input, 2);
                    Expression<Func<Point, Point>> identity = v => v;
                    if (!identity.Compile()(p).Same(p)) return 1;

                    var when = new DateTime(2024, 2, 29, 1, 2, 3, DateTimeKind.Utc);
                    Expression<Func<DateTime?, DateTime?>> date = v => v;
                    if (date.Compile()(null) != null || date.Compile()(when) != when) return 2;

                    // Default and New(Type) give the zero value.
                    var wideDefault = Expression.Lambda<Func<Wide>>(Expression.Default(typeof(Wide))).Compile()();
                    var wideNew = Expression.Lambda<Func<Wide>>(Expression.New(typeof(Wide))).Compile()();
                    if (wideDefault.A != 0 || wideDefault.B != 0 || wideDefault.C != 0 || wideDefault.D != null ||
                        wideNew.A != 0 || wideNew.D != null) return 3;
                    if (Expression.Lambda<Func<DateTime>>(Expression.Default(typeof(DateTime))).Compile()() != default(DateTime))
                        return 4;
                    if (Expression.Lambda<Func<Point?>>(Expression.Default(typeof(Point?))).Compile()() != null) return 5;

                    // A block local starts at its default on every call; assignment keeps the value.
                    var flag = Expression.Parameter(typeof(bool), "flag");
                    var arg = Expression.Parameter(typeof(Point), "arg");
                    var local = Expression.Variable(typeof(Point), "local");
                    var chooser = Expression.Lambda<Func<bool, Point, Point>>(
                        Expression.Block(
                            new[] { local },
                            Expression.IfThen(flag, Expression.Assign(local, arg)),
                            local),
                        flag, arg).Compile();
                    if (!chooser(true, p).Same(p) || !chooser(false, p).Same(default) || !chooser(true, p).Same(p)) return 6;

                    // A constant struct.
                    var constant = Expression.Lambda<Func<Point>>(Expression.Constant(new Point(3, 4))).Compile();
                    if (!constant().Same(new Point(3, 4))) return 7;

                    // Boxing, unboxing and interface conversions.
                    var boxed = Expression.Lambda<Func<Point, object>>(Expression.Convert(arg, typeof(object)), arg).Compile()(p);
                    if (boxed is not Point boxedPoint || !boxedPoint.Same(p)) return 8;
                    var o = Expression.Parameter(typeof(object), "o");
                    var unbox = Expression.Lambda<Func<object, Point>>(Expression.Convert(o, typeof(Point)), o).Compile();
                    if (!unbox(p).Same(p)) return 9;
                    if (!Throws<InvalidCastException>(() => unbox("text")) || !Throws<NullReferenceException>(() => unbox(null!)))
                        return 10;
                    var asShape = Expression.Lambda<Func<Point, IShape>>(Expression.Convert(arg, typeof(IShape)), arg).Compile();
                    if (asShape(p).Area() != input * 2) return 11;
                    var shape = Expression.Parameter(typeof(IShape), "shape");
                    var fromShape = Expression.Lambda<Func<IShape, Point>>(Expression.Convert(shape, typeof(Point)), shape).Compile();
                    if (!fromShape(p).Same(p)) return 12;

                    // Nullable wrapping and unwrapping.
                    var wrap = Expression.Lambda<Func<Point, Point?>>(Expression.Convert(arg, typeof(Point?)), arg).Compile();
                    if (wrap(p) is not { } wrapped || !wrapped.Same(p)) return 13;
                    var maybe = Expression.Parameter(typeof(Point?), "maybe");
                    var unwrap = Expression.Lambda<Func<Point?, Point>>(Expression.Convert(maybe, typeof(Point)), maybe).Compile();
                    if (!unwrap(p).Same(p) || !Throws<InvalidOperationException>(() => unwrap(null))) return 14;

                    // Conditionals and arrays of structs.
                    var other = Expression.Parameter(typeof(Point), "other");
                    var pick = Expression.Lambda<Func<bool, Point, Point, Point>>(
                        Expression.Condition(flag, arg, other), flag, arg, other).Compile();
                    if (!pick(false, p, new Point(5, 6)).Same(new Point(5, 6))) return 15;
                    var pair = Expression.Lambda<Func<Point, Point, Point[]>>(
                        Expression.NewArrayInit(typeof(Point), arg, other), arg, other).Compile()(p, new Point(7, 8));
                    if (pair.Length != 2 || !pair[0].Same(p) || !pair[1].Same(new Point(7, 8))) return 16;
                    var points = Expression.Parameter(typeof(Point[]), "points");
                    var index = Expression.Parameter(typeof(int), "index");
                    var element = Expression.Lambda<Func<Point[], int, Point>>(
                        Expression.ArrayIndex(points, index), points, index).Compile();
                    if (!element(pair, 1).Same(new Point(7, 8))) return 17;

                    // Several value-type parameters of different types.
                    var instant = Expression.Parameter(typeof(DateTime), "instant");
                    var amount = Expression.Parameter(typeof(decimal), "amount");
                    var third = Expression.Lambda<Func<int, Point, DateTime, decimal, DateTime>>(
                        instant, index, arg, instant, amount).Compile();
                    if (third(1, p, when, 2.5m) != when) return 18;

                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionStructFixture.EntryPoint",
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

    // Decimal trees as C# builds them (binary operators without a method, unary minus and conversions with decimal's
    // own methods) and as the factories build them, run with desktop's results, lifting and exceptions.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledDecimalExpressionsRunAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionDecimalFixture",
            """
            using System;
            using System.Linq.Expressions;
            using System.Reflection;

            namespace ExpressionDecimalFixture;

            public static class EntryPoint
            {
                static bool Throws<T>(Func<object?> run) where T : Exception
                {
                    try { run(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    decimal d = input;
                    Expression<Func<decimal, decimal>> arithmetic = m => m * 2m + 0.5m - m / 4m % 3m;
                    if (arithmetic.Compile()(d) != d * 2m + 0.5m - d / 4m % 3m) return 1;
                    Expression<Func<decimal, decimal>> negate = m => -m;
                    Expression<Func<decimal, decimal>> plus = m => +m;
                    if (negate.Compile()(d) != -d || plus.Compile()(-d) != -d) return 2;
                    Expression<Func<int, decimal>> widen = i => i / 4m;
                    Expression<Func<double, decimal>> fromDouble = x => (decimal)x;
                    if (widen.Compile()(input) != input / 4m || fromDouble.Compile()(2.5) != 2.5m) return 3;
                    Expression<Func<decimal, int>> truncate = m => (int)m;
                    Expression<Func<decimal, double>> toDouble = m => (double)m;
                    Expression<Func<decimal, byte>> toByte = m => (byte)m;
                    if (truncate.Compile()(-2.75m) != -2 || toDouble.Compile()(1.25m) != 1.25) return 4;
                    if (!Throws<OverflowException>(() => toByte.Compile()(300m))) return 5;
                    Expression<Func<decimal, bool>> compare = m => m < 2m || m == 2.0m && m != 3m;
                    var compareRun = compare.Compile();
                    if (!compareRun(1m) || !compareRun(2m) || compareRun(d)) return 6;
                    Expression<Func<decimal?, decimal?>> lifted = m => m + 1m;
                    Expression<Func<decimal?, bool>> liftedEqual = m => m == 2m;
                    Expression<Func<decimal?, bool>> liftedLess = m => m < 2m;
                    if (lifted.Compile()(null) != null || lifted.Compile()(1m) != 2m) return 7;
                    if (liftedEqual.Compile()(null) || !liftedEqual.Compile()(2m) || liftedLess.Compile()(null)) return 8;
                    Expression<Func<int?, decimal?>> liftedWiden = i => i;
                    if (liftedWiden.Compile()(null) != null || liftedWiden.Compile()(input) != d) return 9;

                    // The factories supply decimal's operators: op_Addition runs when invoked, and the unary minus is
                    // the method C# names.
                    var p = Expression.Parameter(typeof(decimal), "p");
                    var add = Expression.Add(p, Expression.Constant(1m));
                    if (add.Method is not { } addition || addition.Name != "op_Addition" || addition.DeclaringType != typeof(decimal) ||
                        !addition.IsStatic || (decimal)addition.Invoke(null, new object[] { 1m, 2m })! != 3m) return 10;
                    if (Expression.Lambda<Func<decimal, decimal>>(add, p).Compile()(d) != d + 1m) return 11;
                    if (Expression.Negate(p).Method != ((UnaryExpression)negate.Body).Method) return 12;
                    if (Expression.Lambda<Func<decimal, decimal>>(Expression.Increment(p), p).Compile()(d) != d + 1m) return 13;
                    var i = Expression.Parameter(typeof(int), "i");
                    var convert = Expression.Convert(i, typeof(decimal));
                    if (convert.Method != ((UnaryExpression)((Expression<Func<int, decimal>>)(x => x)).Body).Method ||
                        Expression.Lambda<Func<int, decimal>>(convert, i).Compile()(input) != d) return 14;
                    var max = Expression.Lambda<Func<decimal, decimal>>(Expression.AddChecked(p, Expression.Constant(decimal.MaxValue)), p).Compile();
                    var divide = Expression.Lambda<Func<decimal, decimal>>(Expression.Divide(p, Expression.Constant(0m)), p).Compile();
                    if (!Throws<OverflowException>(() => max(1m)) || !Throws<DivideByZeroException>(() => divide(1m))) return 15;
                    if (!Throws<InvalidOperationException>(() => Expression.And(p, p))) return 16;
                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionDecimalFixture.EntryPoint",
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

    // Arrays: length, element reads and writes in one and two dimensions, creation with one bound or with elements,
    // with desktop's exceptions for a null array, a bad index and a negative length.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledArrayExpressionsReadWriteAndCreate(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionArrayFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionArrayFixture;

            public static class EntryPoint
            {
                static bool Throws<T>(Func<object?> run) where T : Exception
                {
                    try { run(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    Expression<Func<int[], int>> length = a => a.Length;
                    var lengthRun = length.Compile();
                    if (lengthRun(new int[3]) != 3 || !Throws<NullReferenceException>(() => lengthRun(null!))) return 1;

                    Expression<Func<int[], int>> second = a => a[1];
                    var secondRun = second.Compile();
                    if (secondRun(new[] { 4, input }) != input || !Throws<IndexOutOfRangeException>(() => secondRun(new int[1]))) return 2;

                    var grid = Expression.Parameter(typeof(int[,]), "grid");
                    var cell = Expression.Lambda<Func<int[,], int>>(
                        Expression.ArrayAccess(grid, Expression.Constant(1), Expression.Constant(2)), grid).Compile();
                    var values = new int[2, 3];
                    values[1, 2] = input;
                    if (cell(values) != input) return 3;

                    Expression<Func<int, int[]>> sized = n => new int[n];
                    var sizedRun = sized.Compile();
                    if (sizedRun(4).Length != 4 || sizedRun(0).Length != 0 || !Throws<OverflowException>(() => sizedRun(-1))) return 4;

                    Expression<Func<string, string[]>> listed = text => new[] { text, "b" };
                    var listedRun = listed.Compile()("a");
                    if (listedRun.Length != 2 || listedRun[0] != "a" || listedRun[1] != "b") return 5;

                    // Creating a rectangular array is outside the profile: it would root rank 2 and 3 arrays of every
                    // element type in every program that compiles an expression.
                    var count = Expression.Parameter(typeof(int), "count");
                    var rectangular = Expression.Lambda<Func<int, int[,]>>(
                        Expression.NewArrayBounds(typeof(int), count, Expression.Constant(2)), count);
                    if (!Throws<NotSupportedException>(() => rectangular.Compile())) return 6;

                    // An element assignment as a statement and as a value.
                    var array = Expression.Parameter(typeof(int[]), "array");
                    var first = Expression.ArrayAccess(array, Expression.Constant(0));
                    var assign = Expression.Lambda<Func<int[], int>>(
                        Expression.Block(
                            Expression.Assign(first, Expression.Constant(input)),
                            Expression.Add(Expression.Assign(Expression.ArrayAccess(array, Expression.Constant(1)), Expression.Constant(1)), first)),
                        array).Compile();
                    var target = new int[2];
                    if (assign(target) != input + 1 || target[0] != input || target[1] != 1) return 7;
                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionArrayFixture.EntryPoint",
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

    // Try with typed catches, finally on both paths, fault on the exceptional one, rethrow keeping the exception, and
    // the forms outside the profile (filters, a goto leaving a finally's protection) rejected when compiling.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledTryCatchFinallyAndFaultRunAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTryFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionTryFixture;

            public static class EntryPoint
            {
                static bool Throws<T>(Func<object?> run) where T : Exception
                {
                    try { run(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    var n = Expression.Parameter(typeof(int), "n");
                    var log = Expression.Variable(typeof(int), "log");
                    var error = Expression.Variable(typeof(InvalidOperationException), "error");
                    var boom = Expression.Constant(new InvalidOperationException("boom"));
                    var wrong = Expression.Constant(new ArgumentException("wrong"));
                    Expression ThrowWhen(Expression test, Expression exception) =>
                        Expression.IfThen(test, Expression.Throw(exception));

                    // A typed try: the body's value, or the catch's, which reads its variable (the thrown exception).
                    var guarded = Expression.Lambda<Func<int, int>>(
                        Expression.TryCatch(
                            Expression.Block(ThrowWhen(Expression.GreaterThan(n, Expression.Constant(100)), boom), n),
                            Expression.Catch(error, Expression.Condition(Expression.ReferenceEqual(error, boom), Expression.Constant(4), Expression.Constant(-4)))),
                        n).Compile();
                    if (guarded(input) != input || guarded(101) != 4) return 1;

                    // An exception no catch matches passes through.
                    var unmatched = Expression.Lambda<Func<int, int>>(
                        Expression.TryCatch(Expression.Block(Expression.Throw(wrong), n), Expression.Catch(error, n)), n).Compile();
                    if (!Throws<ArgumentException>(() => unmatched(1))) return 2;

                    // Finally runs on the normal path and on the exceptional one, before an outer catch.
                    var finallyRuns = Expression.Lambda<Func<int, int>>(
                        Expression.Block(new[] { log },
                            Expression.TryCatch(
                                Expression.TryFinally(
                                    ThrowWhen(Expression.GreaterThan(n, Expression.Constant(0)), boom),
                                    Expression.AddAssign(log, Expression.Constant(10))),
                                Expression.Catch(typeof(InvalidOperationException), Expression.Block(typeof(void), Expression.AddAssign(log, Expression.Constant(1))))),
                            log),
                        n).Compile();
                    if (finallyRuns(0) != 10 || finallyRuns(1) != 11) return 3;

                    // Fault runs only on the exceptional path.
                    var faultRuns = Expression.Lambda<Func<int, int>>(
                        Expression.Block(new[] { log },
                            Expression.TryCatch(
                                Expression.TryFault(
                                    ThrowWhen(Expression.GreaterThan(n, Expression.Constant(0)), boom),
                                    Expression.AddAssign(log, Expression.Constant(10))),
                                Expression.Catch(typeof(InvalidOperationException), Expression.Block(typeof(void), Expression.AddAssign(log, Expression.Constant(1))))),
                            log),
                        n).Compile();
                    if (faultRuns(0) != 0 || faultRuns(1) != 11) return 4;

                    // A rethrow from a catch reaches the outer catch as the same exception.
                    var caught = Expression.Variable(typeof(Exception), "caught");
                    var rethrown = Expression.Lambda<Func<int, Exception>>(
                        Expression.TryCatch(
                            Expression.Block(
                                Expression.TryCatch(Expression.Throw(boom), Expression.Catch(typeof(Exception), Expression.Rethrow())),
                                Expression.Constant(null, typeof(Exception))),
                            Expression.Catch(caught, caught)),
                        n).Compile();
                    if (!ReferenceEquals(rethrown(0), boom.Value)) return 5;

                    // Filters, and a goto out of a finally's protection, are outside the profile.
                    var filtered = Expression.Lambda<Func<int, int>>(
                        Expression.TryCatch(n, Expression.Catch(error, n, Expression.Constant(true))), n);
                    if (!Throws<NotSupportedException>(() => filtered.Compile())) return 6;
                    var exit = Expression.Label(typeof(int), "exit");
                    var escaping = Expression.Lambda<Func<int, int>>(
                        Expression.Block(
                            Expression.TryFinally(Expression.Return(exit, n), Expression.Empty()),
                            Expression.Label(exit, Expression.Constant(0))),
                        n);
                    if (!Throws<NotSupportedException>(() => escaping.Compile())) return 7;
                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTryFixture.EntryPoint",
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

    // Loops with break and continue, labels and early returns, switches over integers, strings and enums with a value
    // or as statements, and throws, as desktop runs them.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledLoopsSwitchesGotosAndThrowsRunAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionControlFlowStatementsFixture",
            """
            using System;
            using System.Linq.Expressions;

            namespace ExpressionControlFlowStatementsFixture;

            public enum Color { Red, Green, Blue }

            public static class EntryPoint
            {
                static bool Throws<T>(Func<object?> run) where T : Exception
                {
                    try { run(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    var n = Expression.Parameter(typeof(int), "n");
                    var sum = Expression.Variable(typeof(int), "sum");
                    var i = Expression.Variable(typeof(int), "i");

                    // sum of 1..n, leaving through a typed break label.
                    var done = Expression.Label(typeof(int), "done");
                    var total = Expression.Lambda<Func<int, int>>(
                        Expression.Block(new[] { sum, i },
                            Expression.Assign(sum, Expression.Constant(0)),
                            Expression.Assign(i, Expression.Constant(1)),
                            Expression.Loop(
                                Expression.IfThenElse(
                                    Expression.LessThanOrEqual(i, n),
                                    Expression.Block(Expression.AddAssign(sum, i), Expression.PostIncrementAssign(i)),
                                    Expression.Break(done, sum)),
                                done)),
                        n).Compile();
                    if (total(10) != 55 || total(0) != 0) return 1;

                    // continue skips the even numbers; break is untyped.
                    var stop = Expression.Label("stop");
                    var next = Expression.Label("next");
                    var odd = Expression.Lambda<Func<int, int>>(
                        Expression.Block(new[] { sum, i },
                            Expression.Assign(sum, Expression.Constant(0)),
                            Expression.Assign(i, Expression.Constant(0)),
                            Expression.Loop(
                                Expression.Block(
                                    Expression.PreIncrementAssign(i),
                                    Expression.IfThen(Expression.GreaterThan(i, n), Expression.Break(stop)),
                                    Expression.IfThen(Expression.Equal(Expression.Modulo(i, Expression.Constant(2)), Expression.Constant(0)), Expression.Continue(next)),
                                    Expression.AddAssign(sum, i)),
                                stop, next),
                            sum),
                        n).Compile();
                    if (odd(10) != 25) return 2;

                    // An early return through a typed label at the end of the body.
                    var result = Expression.Label(typeof(int), "result");
                    var clamp = Expression.Lambda<Func<int, int>>(
                        Expression.Block(
                            Expression.IfThen(Expression.GreaterThan(n, Expression.Constant(100)), Expression.Return(result, Expression.Constant(100))),
                            Expression.Label(result, n)),
                        n).Compile();
                    if (clamp(250) != 100 || clamp(input) != input) return 3;

                    // A switch with several test values per case and a default.
                    var classify = Expression.Lambda<Func<int, string>>(
                        Expression.Switch(n, Expression.Constant("other"),
                            Expression.SwitchCase(Expression.Constant("small"), Expression.Constant(1), Expression.Constant(2)),
                            Expression.SwitchCase(Expression.Constant("answer"), Expression.Constant(42))),
                        n).Compile();
                    if (classify(2) != "small" || classify(input + 1) != "answer" || classify(7) != "other") return 4;

                    var word = Expression.Parameter(typeof(string), "word");
                    var greet = Expression.Lambda<Func<string, int>>(
                        Expression.Switch(word, Expression.Constant(0),
                            Expression.SwitchCase(Expression.Constant(1), Expression.Constant("hello")),
                            Expression.SwitchCase(Expression.Constant(2), Expression.Constant("bye"))),
                        word).Compile();
                    if (greet("hel" + "lo") != 1 || greet("bye") != 2 || greet("x") != 0 || greet(null!) != 0) return 5;

                    var color = Expression.Parameter(typeof(Color), "color");
                    var rank = Expression.Lambda<Func<Color, int>>(
                        Expression.Switch(color, Expression.Constant(-1),
                            Expression.SwitchCase(Expression.Constant(10), Expression.Constant(Color.Green)),
                            Expression.SwitchCase(Expression.Constant(20), Expression.Constant(Color.Blue))),
                        color).Compile();
                    if (rank(Color.Blue) != 20 || rank(Color.Red) != -1) return 6;

                    // A switch as a statement that assigns.
                    var assigned = Expression.Lambda<Func<int, int>>(
                        Expression.Block(new[] { sum },
                            Expression.Assign(sum, Expression.Constant(-1)),
                            Expression.Switch(typeof(void), n, null, null,
                                Expression.SwitchCase(Expression.Assign(sum, Expression.Constant(7)), Expression.Constant(3))),
                            sum),
                        n).Compile();
                    if (assigned(3) != 7 || assigned(4) != -1) return 7;

                    // A throw typed as a value, and a null throw.
                    var guard = Expression.Lambda<Func<int, int>>(
                        Expression.Condition(
                            Expression.GreaterThan(n, Expression.Constant(100)),
                            Expression.Throw(Expression.Constant(new InvalidOperationException("too big")), typeof(int)),
                            n),
                        n).Compile();
                    if (guard(5) != 5 || !Throws<InvalidOperationException>(() => guard(101))) return 8;
                    var throwsNull = Expression.Lambda<Func<int, int>>(
                        Expression.Block(Expression.Throw(Expression.Constant(null, typeof(Exception))), n), n).Compile();
                    if (!Throws<NullReferenceException>(() => throwsNull(1))) return 9;
                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionControlFlowStatementsFixture.EntryPoint",
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

    // Type tests and reference, boxing and unboxing conversions run on the runtime type table, with desktop's
    // null and failure semantics.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompiledTypeTestsAndConversionsUseTheTypeTable(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ExpressionTypeTestFixture",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq.Expressions;

            namespace ExpressionTypeTestFixture;

            public interface IShape { }
            public class Base : IShape { }
            public sealed class Derived : Base { }

            public static class EntryPoint
            {
                static bool Throws<T>(Func<object?> run) where T : Exception
                {
                    try { run(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    Expression<Func<object?, bool>> isString = o => o is string;
                    var isStringRun = isString.Compile();
                    if (!isStringRun("text") || isStringRun(5) || isStringRun(null)) return 1;

                    Expression<Func<Base?, bool>> isDerived = b => b is Derived;
                    var isDerivedRun = isDerived.Compile();
                    if (!isDerivedRun(new Derived()) || isDerivedRun(new Base()) || isDerivedRun(null)) return 2;

                    var item = Expression.Parameter(typeof(object), "item");
                    var exactly = Expression.Lambda<Func<object?, bool>>(Expression.TypeEqual(item, typeof(Base)), item).Compile();
                    if (!exactly(new Base()) || exactly(new Derived()) || exactly(null)) return 3;

                    Expression<Func<object?, string?>> asString = o => o as string;
                    var asStringRun = asString.Compile();
                    if (asStringRun("text") != "text" || asStringRun(5) != null) return 4;

                    Expression<Func<object?, int?>> asNullable = o => o as int?;
                    var asNullableRun = asNullable.Compile();
                    if (asNullableRun(7) != 7 || asNullableRun("seven") != null) return 5;

                    // A downcast and an interface cast check the type; null passes.
                    Expression<Func<object?, string?>> toString = o => (string?)o;
                    var toStringRun = toString.Compile();
                    if (toStringRun("text") != "text" || toStringRun(null) != null) return 6;
                    if (!Throws<InvalidCastException>(() => toStringRun(5))) return 7;
                    Expression<Func<object?, IShape?>> toShape = o => (IShape?)o;
                    var toShapeRun = toShape.Compile();
                    var derived = new Derived();
                    if (!ReferenceEquals(toShapeRun(derived), derived) || !Throws<InvalidCastException>(() => toShapeRun("x"))) return 8;

                    // Unboxing: null throws for a value type and passes for a nullable one.
                    Expression<Func<object?, int>> unbox = o => (int)o!;
                    var unboxRun = unbox.Compile();
                    if (unboxRun(input) != input) return 9;
                    if (!Throws<NullReferenceException>(() => unboxRun(null)) || !Throws<InvalidCastException>(() => unboxRun("x"))) return 10;
                    var unboxNode = Expression.Lambda<Func<object, int>>(Expression.Unbox(item, typeof(int)), item).Compile();
                    if (unboxNode(3) != 3 || !Throws<InvalidCastException>(() => unboxNode(3L))) return 11;
                    Expression<Func<object?, int?>> unboxNullable = o => (int?)o;
                    if (unboxNullable.Compile()(null) != null) return 12;

                    // Boxing and widening conversions keep the value.
                    Expression<Func<int, object>> box = x => x;
                    if (!(box.Compile()(input) is int boxed) || boxed != input) return 13;
                    Expression<Func<Derived, Base>> widen = d => d;
                    if (!ReferenceEquals(widen.Compile()(derived), derived)) return 14;

                    // An array's length; a null array throws as ldlen does.
                    Expression<Func<int[]?, int>> length = a => a!.Length;
                    var lengthRun = length.Compile();
                    if (lengthRun(new int[3]) != 3 || !Throws<NullReferenceException>(() => lengthRun(null))) return 16;

                    // Covariance: a List<string> is an IEnumerable<object>.
                    Expression<Func<object?, bool>> isEnumerable = o => o is IEnumerable<object>;
                    if (!isEnumerable.Compile()(new List<string>()) || isEnumerable.Compile()(new List<int>())) return 15;
                    return input + 1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionTypeTestFixture.EntryPoint",
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

                    // A user-defined operator supplied as a MethodInfo runs as a call.
                    Expression<Func<OperatorValue, OperatorValue>> operatorTree =
                        value => value + value;
                    if (operatorTree.Compile()(new OperatorValue(21)).Value != 42)
                        return 22;

                    // A nested lambda captures the outer parameter.
                    Expression<Func<int, Func<int>>> nestedTree = value => () => value;
                    if (nestedTree.Compile()(input)() != input)
                        return 23;

                    // A struct's property runs on the struct.
                    Expression<Func<StructValue, int>> structMemberTree =
                        value => value.Value;
                    if (structMemberTree.Compile()(new StructValue(input)) != input) return 24;

                    Expression<Func<OperatorValue, int>> conversionTree =
                        value => (int)value;
                    if (conversionTree.Compile()(new OperatorValue(42)) != 42)
                        return 28;

                    // A constructor runs on a new object.
                    Expression<Func<int, Model>> constructorTree =
                        value => new Model(value);
                    if (constructorTree.Compile()(input).Number != input) return 29;

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
            TestAssets.RepositorySdkVersion,
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
            TestAssets.RepositorySdkVersion,
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
            TestAssets.RepositorySdkVersion,
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
            TestAssets.RepositorySdkVersion,
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
        var invoke = Assert.Single(
            compilation.Program.MethodDescriptors.Values,
            candidate => candidate.Definition.Name == "Invoke" &&
                candidate.DeclaringType.Shape ==
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
            TestAssets.RepositorySdkVersion,
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

                    // Operators undefined for primitive operands fail as upstream, decimal's operators are found as
                    // upstream finds them, and user-defined conditional logic validates first.
                    var unsupportedOperators = 0;
                    try
                    {
                        _ = Expression.Add(
                            Expression.Constant((byte)1),
                            Expression.Constant((byte)2));
                    }
                    catch (InvalidOperationException)
                    {
                        unsupportedOperators++;
                    }
                    try
                    {
                        _ = Expression.Negate(Expression.Constant((uint)1));
                    }
                    catch (InvalidOperationException)
                    {
                        unsupportedOperators++;
                    }
                    // (Their DeclaringType, not their Name: reading a member's name would deploy every name.)
                    if (Expression.Add(Expression.Constant(1m), Expression.Constant(2m)).Method?.DeclaringType != typeof(decimal) ||
                        Expression.Convert(Expression.Constant(1m), typeof(int)).Method?.DeclaringType != typeof(decimal))
                        return 25;
                    try
                    {
                        _ = Expression.AndAlso(
                            customOperator.Parameters[0],
                            Expression.Constant(1),
                            userAdd.Method);
                    }
                    catch (ArgumentException)
                    {
                        unsupportedOperators++;
                    }
                    if (unsupportedOperators != 3)
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
            TestAssets.RepositorySdkVersion,
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
            compilation.Program.MethodDescriptors.Values,
            candidate => candidate.Definition.Name == "Invoke" &&
                candidate.DeclaringType.Shape ==
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
                    // Type names read as type IDs without names (issue 225); expression text uses them.
                    var modelName = typeof(Model).Name;
                    if (staticMember.Member.DeclaringType != typeof(Model))
                        return 15;
                    if (staticMember.Expression is not null)
                        return 16;
                    if (staticFieldText != "() => " + modelName + ".StaticField")
                    {
                        if (staticFieldText == "() => StaticField")
                            return 17;
                        if (staticFieldText == modelName + ".StaticField")
                            return 18;
                        return 100 + staticFieldText.Length;
                    }
                    if (
                        created.ToString() != "value => new " + modelName + "(value)" ||
                        call.ToString() != "model => model.Add(1)" ||
                        customOperator.ToString() != "model => (model + 1)" ||
                        conversion.ToString() != "model => Convert(model, " + typeof(int).Name + ")" ||
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
                        Expression.Empty().ToString() != "default(" + typeof(void).Name + ")")
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
            TestAssets.RepositorySdkVersion,
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
        Assert.DoesNotContain(
            compilation.Program.Methods.Values,
            method => method.Method.Definition.Name is
                "get_Property" or "Add" or "op_Addition" or "op_Explicit" &&
                method.Method.Definition.Key.Assembly.Name ==
                    "ExpressionTreeVisitorFixture");
    }
}
