using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class AttributeQueryCompilationTests
{
    [Fact]
    public void QueryMethodTokensInExpressionTreesFailAtTheOriginalCallSite()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AttributeMethodToken", """
            using System;
            using System.Linq.Expressions;
            using System.Reflection;
            namespace AttributeMethodToken;
            public sealed class MarkerAttribute : Attribute;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<MemberInfo, MarkerAttribute>> query =
                        member => member.GetCustomAttribute<MarkerAttribute>();
                    return query == null ? 1 : input;
                }
            }
            """);
        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "AttributeMethodToken.EntryPoint", "Run", [])));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
        Assert.Contains("query method handles", error.Diagnostic.Message);
        Assert.Contains("Run", error.Diagnostic.Method);
        Assert.NotNull(error.Diagnostic.IlOffset);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PublicQueryAliasesLowerWithoutRuntimeQueryHelpers(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = """
            using System;
            using System.Reflection;
            namespace AttributeAliases;
            public sealed class MarkerAttribute : Attribute;
            [Marker] public sealed class Target;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var inherit = input > 0;
                    MemberInfo member = typeof(Target);
                    ICustomAttributeProvider provider = typeof(Target);
                    if (!member.IsDefined(typeof(MarkerAttribute), inherit) ||
                        !provider.IsDefined(typeof(MarkerAttribute), inherit)) return 1;
                    if (member.GetCustomAttributes(typeof(MarkerAttribute), inherit).Length != 1 ||
                        provider.GetCustomAttributes(typeof(MarkerAttribute), inherit).Length != 1) return 2;
                    if (Attribute.GetCustomAttribute(typeof(Target), typeof(MarkerAttribute)) is not MarkerAttribute ||
                        Attribute.GetCustomAttribute(typeof(Target), typeof(MarkerAttribute), inherit) is not MarkerAttribute) return 3;
                    if (Attribute.GetCustomAttributes(typeof(Target), typeof(MarkerAttribute)).Length != 1 ||
                        Attribute.GetCustomAttributes(typeof(Target), typeof(MarkerAttribute), inherit).Length != 1) return 4;
                    if (!Attribute.IsDefined(typeof(Target), typeof(MarkerAttribute)) ||
                        !Attribute.IsDefined(typeof(Target), typeof(MarkerAttribute), inherit)) return 5;
                    if (CustomAttributeExtensions.GetCustomAttribute(member, typeof(MarkerAttribute)) is not MarkerAttribute ||
                        CustomAttributeExtensions.GetCustomAttribute(member, typeof(MarkerAttribute), inherit) is not MarkerAttribute) return 6;
                    var count = 0;
                    foreach (var item in CustomAttributeExtensions.GetCustomAttributes(member, typeof(MarkerAttribute)))
                        if (item is MarkerAttribute) count++;
                    foreach (var item in CustomAttributeExtensions.GetCustomAttributes(member, typeof(MarkerAttribute), inherit))
                        if (item is MarkerAttribute) count++;
                    if (count != 2 || !CustomAttributeExtensions.IsDefined(member, typeof(MarkerAttribute)) ||
                        !CustomAttributeExtensions.IsDefined(member, typeof(MarkerAttribute), inherit)) return 7;
                    return input;
                }
            }
            """;
        var assembly = optimized ? assets.CompileOptimizedSource("AttributeAliases", source) :
            assets.CompileSource("AttributeAliases", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "AttributeAliases.EntryPoint", "Run", [], Target: target));
        Assert.DoesNotContain(compilation.Program.MethodInstances.Values, method =>
            method.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeCustomAttributes");
        foreach (var input in new[] { 42, -42 })
            Assert.Equal(input, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
                input, target, compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void QueryExpansionPreservesSourceLinesThroughSwitches(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb("AttributeSourceLines", """
            using System;
            using System.Reflection;
            namespace AttributeSourceLines;

            public sealed class MarkerAttribute(int value) : Attribute
            {
                public int Value { get; } = value;
            }
            [Marker(42)] public sealed class Target;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    try { FailAfterQuery<Target>(input); }
                    catch (InvalidOperationException error)
                    {
                        return error.StackTrace is not null &&
                            error.StackTrace.Contains("QueryTrace.cs:line 303") ? 42 : 1;
                    }
                    return 2;
                }

                private static void FailAfterQuery<T>(int input)
                {
                    var selected = input switch { 0 => 7, 1 => 11, 2 => 13, 3 => 17, _ => 42 };
            #line 101 "Safe/QueryTrace.cs"
                    var attribute = typeof(T).GetCustomAttribute<MarkerAttribute>();
            #line default
                    if (attribute is null || attribute.Value != selected) return;
            #line 303 "Safe/QueryTrace.cs"
                    throw new InvalidOperationException("after query");
            #line default
                }
            }
            """, optimized);
        var options = new CompilerOptions(assembly, [assets.CoreLib], "AttributeSourceLines.EntryPoint",
            "Run", [], Target: target, EmitStackTrace: true, ProjectDirectory: assets.Directory);

        var compilation = NetWasmCompiler.Compile(options);
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
            42, target, compilation.StaticDataEnd, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
            timeZoneAssetPath: null, methodSymbols: compilation.MethodSymbols!.Bytes));
    }

    [Theory]
    [InlineData("(input == 42 ? typeof(Target) : typeof(Other)).GetCustomAttribute<MarkerAttribute>()")]
    [InlineData("typeof(Target).GetCustomAttribute(input == 42 ? typeof(MarkerAttribute) : typeof(OtherAttribute))")]
    [InlineData("typeof(Target).GetCustomAttribute<Attribute>()")]
    [InlineData("typeof(Target).GetCustomAttribute<SerializableAttribute>()")]
    [InlineData("typeof(Target).GetCustomAttribute<System.Runtime.InteropServices.StructLayoutAttribute>()")]
    [InlineData("typeof(Target).GetCustomAttribute<System.Runtime.InteropServices.ComImportAttribute>()")]
    [InlineData("typeof(Target).GetCustomAttributes(true)")]
    [InlineData("CustomAttributeExtensions.GetCustomAttributes(typeof(Target))")]
    [InlineData("CustomAttributeExtensions.GetCustomAttributes(typeof(Target), false)")]
    [InlineData("((ICustomAttributeProvider)typeof(Target)).GetCustomAttributes(false)")]
    [InlineData("new OtherMember().GetCustomAttribute<MarkerAttribute>()")]
    [InlineData("new Target().GetType().GetCustomAttribute<MarkerAttribute>()")]
    [InlineData("((Type)null).GetCustomAttribute<MarkerAttribute>()")]
    [InlineData("typeof(Target).GetCustomAttribute((Type)null)")]
    [InlineData("new Func<MemberInfo, MarkerAttribute>(CustomAttributeExtensions.GetCustomAttribute<MarkerAttribute>)(typeof(Target))")]
    public void UnboundedQueriesFailAtTheirSourceCall(string expression)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("RejectedAttributeQuery", $$"""
            using System;
            using System.Reflection;
            namespace RejectedAttributeQuery;
            public sealed class MarkerAttribute : Attribute;
            public sealed class OtherAttribute : Attribute;
            public sealed class Target;
            public sealed class Other;
            public sealed class OtherMember : MemberInfo;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    _ = {{expression}};
                    return input;
                }
            }
            """);

        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "RejectedAttributeQuery.EntryPoint", "Run", [])));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
        Assert.Contains("Run", error.Diagnostic.Method);
        Assert.NotNull(error.Diagnostic.IlOffset);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UserAttributePayloadsPreserveSerializedTypesAndInheritedGenericMembers(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AttributePayloads", """
            using System;
            using System.Reflection;
            namespace AttributePayloads;

            public enum Small : byte { One = 7 }
            public enum Huge : ulong { Top = 0xffffffffffffffff }
            public class Container<T>;
            public class Outer { public class Inner<T>; }
            public class BaseAttribute<T> : Attribute
            {
                public T Number;
                public T Property { get; set; }
            }
            public sealed class PayloadAttribute : BaseAttribute<int>
            {
                private new int Number;
                private new int Property { get; set; }

                public PayloadAttribute(object scalar, object large, object type, object vector,
                    object[] values, Type closed, Type nested, Type array)
                {
                    Scalar = scalar; Large = large; Type = type; Vector = vector;
                    Values = values; Closed = closed; Nested = nested; Array = array;
                }
                public object Scalar, Large, Type, Vector;
                public object[] Values;
                public Type Closed, Nested, Array;
            }
            [Payload((byte)7, Huge.Top, typeof(Container<int>), new int[] { 3, 5 },
                new object[] { (short)-7, Small.One, typeof(int[]), null, "text", new string[] { "x", null } },
                typeof(Container<string[]>), typeof(Outer.Inner<long>), typeof(int[]),
                Number = 41, Property = 43)]
            public sealed class Target;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = typeof(Target).GetCustomAttribute<PayloadAttribute>();
                    if (value is null || (byte)value.Scalar != 7 || (Huge)value.Large != Huge.Top) return 1;
                    if ((Type)value.Type != typeof(Container<int>)) return 2;
                    if (value.Closed != typeof(Container<string[]>) ||
                        value.Nested != typeof(Outer.Inner<long>) || value.Array != typeof(int[])) return 3;
                    var vector = (int[])value.Vector;
                    if (vector.Length != 2 || vector[0] != 3 || vector[1] != 5) return 4;
                    var values = value.Values;
                    if (values.Length != 6 || (short)values[0] != -7 || (Small)values[1] != Small.One ||
                        (Type)values[2] != typeof(int[]) || values[3] is not null || (string)values[4] != "text") return 5;
                    var strings = (string[])values[5];
                    if (strings.Length != 2 || strings[0] != "x" || strings[1] is not null) return 6;
                    if (value.Number != 41 || value.Property != 43) return 7;
                    return input;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "AttributePayloads.EntryPoint", "Run", [], Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
            42, target, compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ReadonlyTypeFieldsSupportEnumAndNullableEnumQueries(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = """
            using System;
            using System.Reflection;
            namespace ReadonlyAttributeQuery;

            [Flags] public enum Options { One = 1 }
            public enum Plain { One = 1 }
            public sealed class Validator<T>
            {
                private readonly Type _type = typeof(T);
                public bool Check(bool inherit)
                {
                    var type = Nullable.GetUnderlyingType(_type) ?? _type;
                    return type.GetCustomAttribute<FlagsAttribute>(inherit) is not null;
                }
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var inherit = input == 42;
                    if (!new Validator<Options>().Check(inherit)) return 1;
                    if (!new Validator<Options?>().Check(inherit)) return 2;
                    if (new Validator<Plain>().Check(inherit)) return 3;
                    if (new Validator<Plain?>().Check(inherit)) return 4;
                    return input;
                }
            }
            """;
        var assembly = optimized ? assets.CompileOptimizedSource("ReadonlyAttributeQuery", source) :
            assets.CompileSource("ReadonlyAttributeQuery", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "ReadonlyAttributeQuery.EntryPoint", "Run", [], Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
            42, target, compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ExactQueriesRetainOnlyTheirSelectedGenericUserAttributes(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ExactAttributeRetention", """
            using System;
            using System.Reflection;
            namespace ExactAttributeRetention;

            public sealed class MarkerAttribute<T>(string text) : Attribute
            {
                public string Text { get; } = text;
            }
            [Marker<int>("base-only-retention-sentinel")]
            public class Base;
            public sealed class Derived : Base;
            [Marker<int>("selected-payload")]
            public sealed class Selected;
            [Marker<int>("unrelated-target-retention-sentinel")]
            public sealed class Unrelated;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    // Keep the unrelated runtime type present independently.
                    if (typeof(Unrelated) == typeof(Selected)) return 1;
                    if (typeof(Derived).GetCustomAttribute<MarkerAttribute<int>>(false) is not null) return 2;
                    var attribute = typeof(Selected).GetCustomAttribute<MarkerAttribute<int>>(false);
                    return attribute?.Text == "selected-payload" ? input : 3;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "ExactAttributeRetention.EntryPoint", "Run", [], Target: target));

        Assert.DoesNotContain("base-only-retention-sentinel", compilation.Program.StringLiterals);
        Assert.DoesNotContain("unrelated-target-retention-sentinel", compilation.Program.StringLiterals);
        Assert.Contains("selected-payload", compilation.Program.StringLiterals);
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
            42, target, compilation.StaticDataEnd));
    }


    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SingularAmbiguityPreservesConstructorAndSetterSideEffects(WasmTarget target)
    {
        AmbiguityProbe.Calls = 0;
        Assert.Throws<System.Reflection.AmbiguousMatchException>(() =>
            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<AmbiguityAttribute>(
                typeof(AmbiguityTarget)));
        Assert.Equal(1234, AmbiguityProbe.Calls);

        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AttributeAmbiguity", """
            using System;
            using System.Reflection;

            namespace AttributeAmbiguity;

            public static class Probe
            {
                public static int Calls;
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public sealed class MarkerAttribute : Attribute
            {
                public MarkerAttribute(int value) => Probe.Calls = Probe.Calls * 10 + value;

                public int Label
                {
                    get;
                    set
                    {
                        field = value;
                        Probe.Calls = Probe.Calls * 10 + value;
                    }
                }
            }

            [Marker(1, Label = 2)]
            [Marker(3, Label = 4)]
            public sealed class Decorated;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    try
                    {
                        _ = typeof(Decorated).GetCustomAttribute<MarkerAttribute>();
                        return 1;
                    }
                    catch (AmbiguousMatchException)
                    {
                        return Probe.Calls == 1234 ? input : 2;
                    }
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "AttributeAmbiguity.EntryPoint", "Run", [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 42, target,
            compilation.StaticDataEnd));
    }

    private static class AmbiguityProbe
    {
        internal static int Calls;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    private sealed class AmbiguityAttribute : Attribute
    {
        public AmbiguityAttribute(int value) =>
            AmbiguityProbe.Calls = AmbiguityProbe.Calls * 10 + value;

        public int Label
        {
            get;
            set
            {
                field = value;
                AmbiguityProbe.Calls = AmbiguityProbe.Calls * 10 + value;
            }
        }
    }

    [Ambiguity(1, Label = 2)]
    [Ambiguity(3, Label = 4)]
    private sealed class AmbiguityTarget;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void BoundedTypeQueriesReturnFlagsAndDescriptions(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = """
            using System;
            using System.ComponentModel;
            using System.Reflection;

            namespace AttributeQueryFixture;

            [Flags]
            public enum Options
            {
                None = 0,
                One = 1,
            }

            [Description("base-description")]
            public class BaseModel;

            public sealed class DerivedModel : BaseModel;

            public sealed class PlainModel;

            public enum Severity : byte
            {
                Low = 1,
                High = 2,
            }

            [AttributeUsage(AttributeTargets.Class, Inherited = true)]
            public sealed class MarkerAttribute : Attribute
            {
                public MarkerAttribute(string text, int number, Type relatedType)
                {
                    Text = text;
                    Number = number;
                    RelatedType = relatedType;
                }

                public string Text { get; }
                public int Number { get; }
                public Type RelatedType { get; }
                public string Label { get; set; } = string.Empty;
                public Severity Severity { get; set; }
                public int Code;
            }

            [Marker("user-payload", 7, typeof(PlainModel),
                Label = "named-value", Severity = Severity.High, Code = 11)]
            public class CustomBase;

            public sealed class CustomDerived : CustomBase;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public sealed class MultiAttribute : Attribute
            {
                public MultiAttribute(int value) => Value = value;

                public int Value { get; }
            }

            [Multi(1)]
            [Multi(2)]
            public sealed class MultiTarget;

            public sealed class MutableDescription : DescriptionAttribute
            {
                public MutableDescription(string value) : base(value) { }

                public void Set(string value) => DescriptionValue = value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (typeof(Options).GetCustomAttribute<FlagsAttribute>() is null)
                        return 1;
                    if (!typeof(Options).IsDefined(typeof(FlagsAttribute), false))
                        return 2;
                    if (typeof(PlainModel).GetCustomAttribute<FlagsAttribute>() is not null)
                        return 3;

                    var direct = typeof(BaseModel)
                        .GetCustomAttribute<DescriptionAttribute>();
                    if (direct?.Description != "base-description")
                        return 4;
                    if (typeof(DerivedModel)
                        .GetCustomAttribute<DescriptionAttribute>(false) is not null)
                        return 5;
                    var inherited = typeof(DerivedModel)
                        .GetCustomAttribute<DescriptionAttribute>(true);
                    if (inherited?.Description != "base-description")
                        return 6;

                    var genericMany = (DescriptionAttribute[])typeof(BaseModel)
                        .GetCustomAttributes<DescriptionAttribute>();
                    if (genericMany.Length != 1 ||
                        genericMany[0].Description != "base-description")
                        return 7;
                    var filtered = typeof(BaseModel)
                        .GetCustomAttributes(typeof(DescriptionAttribute), false);
                    if (filtered.Length != 1 ||
                        ((DescriptionAttribute)filtered[0]).Description !=
                            "base-description")
                        return 8;

                    var empty = new DescriptionAttribute();
                    if (empty.Description != string.Empty ||
                        !empty.IsDefaultAttribute() ||
                        !empty.Equals(DescriptionAttribute.Default))
                        return 9;
                    var first = new DescriptionAttribute("value");
                    var second = new DescriptionAttribute("value");
                    if (!first.Equals(second) ||
                        first.GetHashCode() != second.GetHashCode() ||
                        first.IsDefaultAttribute())
                        return 10;
                    var mutable = new MutableDescription("before");
                    mutable.Set("after");
                    if (mutable.Description != "after")
                        return 11;

                    var marker = typeof(CustomDerived)
                        .GetCustomAttribute<MarkerAttribute>(true);
                    if (marker is null || marker.Text != "user-payload" ||
                        marker.Number != 7 || marker.Label != "named-value" ||
                        marker.RelatedType != typeof(PlainModel) ||
                        marker.Severity != Severity.High || marker.Code != 11)
                        return 12;
                    var secondMarker = typeof(CustomDerived)
                        .GetCustomAttribute<MarkerAttribute>(true);
                    if (secondMarker is null || object.ReferenceEquals(marker, secondMarker))
                        return 13;
                    if (!typeof(CustomBase).IsDefined(typeof(MarkerAttribute), false))
                        return 14;
                    var markerArray = (MarkerAttribute[])typeof(CustomBase)
                        .GetCustomAttributes<MarkerAttribute>();
                    if (markerArray.Length != 1 ||
                        markerArray[0].Text != "user-payload")
                        return 15;
                    var nonGenericMarker = typeof(CustomBase)
                        .GetCustomAttribute(typeof(MarkerAttribute));
                    if (nonGenericMarker is not MarkerAttribute value || value.Code != 11)
                        return 16;
                    var staticHelper = Attribute.GetCustomAttribute(
                        typeof(CustomBase),
                        typeof(MarkerAttribute),
                        false);
                    if (staticHelper is not MarkerAttribute helperValue ||
                        helperValue.Text != "user-payload")
                        return 17;
                    var many = (MultiAttribute[])typeof(MultiTarget)
                        .GetCustomAttributes<MultiAttribute>();
                    if (many.Length != 2 || many[0].Value != 1 || many[1].Value != 2)
                        return 18;
                    try
                    {
                        _ = typeof(MultiTarget).GetCustomAttribute<MultiAttribute>();
                        return 19;
                    }
                    catch (AmbiguousMatchException)
                    {
                    }

                    return input;
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("AttributeQueryFixture", source)
            : assets.CompileSource("AttributeQueryFixture", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "AttributeQueryFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            42,
            target,
            compilation.StaticDataEnd));
    }

    [Fact]
    public void UnqueriedAttributesDoNotEnterTheQueryPlan()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AttributeRetentionControl",
            """
            using System.ComponentModel;

            namespace AttributeRetentionControl;

            [Description("attribute-retention-sentinel")]
            public sealed class Decorated;

            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "AttributeRetentionControl.EntryPoint",
            "Run",
            []));

        Assert.DoesNotContain(
            "attribute-retention-sentinel",
            compilation.Program.StringLiterals);
    }

    [Fact]
    public void IsDefinedDoesNotRetainAttributeConstructionPayload()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AttributePresenceControl",
            """
            using System;

            namespace AttributePresenceControl;

            public sealed class MarkerAttribute : Attribute
            {
                public MarkerAttribute(string value) => Value = value;

                public string Value { get; }
            }

            [Marker("is-defined-construction-sentinel")]
            public sealed class Decorated;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    typeof(Decorated).IsDefined(typeof(MarkerAttribute), false)
                        ? input
                        : 1;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "AttributePresenceControl.EntryPoint",
            "Run",
            []));

        Assert.DoesNotContain(
            "is-defined-construction-sentinel",
            compilation.Program.StringLiterals);
        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            42,
            WasmTarget.Wasm32,
            compilation.StaticDataEnd));
    }
}
