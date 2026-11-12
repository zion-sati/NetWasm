using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// One site that many types reach (a dispatch, a type test, a delegate's targets, a method that
// touches many types' statics) emits code that grows linearly with the types, never with their
// square. Issue 222 was one such site: Convert.ChangeType over boxed enums grew quadratically and
// failed the function size limit at about 115 enums. Doubling the types reaching each construct
// here must at most double its largest function, give or take the fixed part.
public sealed class PerTypeCodeGrowthTests
{
    private const int Types = 32;
    private const double Linear = 2.4;

    public static TheoryData<string> Constructs => [.. Programs.Keys];

    [Theory]
    [MemberData(nameof(Constructs))]
    public void TheLargestFunctionGrowsLinearlyWithTheTypesReachingOneSite(string construct)
    {
        var small = LargestFunction(construct, Types);
        var large = LargestFunction(construct, Types * 2);

        Assert.True(large <= small * Linear,
            $"{construct}: the largest function is {small} B with {Types} types and {large} B with {Types * 2}");
    }

    private static int LargestFunction(string construct, int count)
    {
        using var assets = TestAssets.Create();
        var (types, body) = Programs[construct](count);
        var assembly = assets.CompileOptimizedSource($"Growth{construct}{count}", $$"""
            using System;
            using System.Collections.Generic;

            public interface IShape { int Area(); }

            public abstract class Base
            {
                public abstract int Measure<T>();
            }

            {{types}}

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    long sum = 0;
                    {{body}}
                    return (int)(sum ^ (sum >> 32));
                }
            }
            """);
        var module = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O0)).ApplicationModule;
        return WasmCodePatcher.Bodies(module)!.Max(function => function.Length);
    }

    private static string Each(int count, Func<int, string> declaration) =>
        string.Join("\n", Enumerable.Range(0, count).Select(declaration));

    private static string List(int count, Func<int, string> element) =>
        string.Join(", ", Enumerable.Range(0, count).Select(element));

    private static string Enums(int count) => Each(count, index => $"public enum E{index} {{ A = {index}, B }}");

    private static string Structs(int count) =>
        Each(count, index => $"public struct S{index} {{ public int X; public long Y; public string? Z; }}");

    private static readonly Dictionary<string, Func<int, (string Types, string Body)>> Programs = new()
    {
        // Issue 222's site.
        ["EnumConvert"] = count => (Enums(count), $$"""
            object[] values = { {{List(count, index => $"E{index}.B")}} };
            foreach (var value in values) sum += (long)Convert.ChangeType(value, typeof(long));
            """),
        ["EnumObjectMembers"] = count => (Enums(count), $$"""
            object[] values = { {{List(count, index => $"E{index}.A")}} };
            foreach (var value in values)
                sum += value.ToString()!.Length + value.GetHashCode() + (value.Equals(values[0]) ? 1 : 0) +
                    ((IComparable)value).CompareTo(value);
            """),
        ["EnumFormatting"] = count => (Enums(count), $$"""
            object[] values = { {{List(count, index => $"E{index}.A")}} };
            foreach (var value in values) sum += $"{value:G}".Length + string.Format("{0:D}", value).Length;
            """),
        ["StructObjectMembers"] = count => (Structs(count), $$"""
            object[] values = { {{List(count, index => $"new S{index} {{ X = {index} }}")}} };
            foreach (var value in values)
                sum += value.GetHashCode() + (value.Equals(values[0]) ? 1 : 0) + value.ToString()!.Length;
            """),
        // Each struct's base.Equals and base.GetHashCode reach only that struct.
        ["StructBaseCalls"] = count => (Each(count, index => $$"""
            public struct S{{index}}
            {
                public int X;
                public string? Z;
                public override bool Equals(object? other) => base.Equals(other);
                public override int GetHashCode() => base.GetHashCode();
            }
            """), $$"""
            object[] values = { {{List(count, index => $"new S{index} {{ X = {index} }}")}} };
            foreach (var value in values) sum += value.GetHashCode() + (value.Equals(values[0]) ? 1 : 0);
            """),
        ["EqualityComparer"] = count => (Structs(count), $$"""
            object[] values = { {{List(count, index => $"new S{index} {{ X = {index} }}")}} };
            var comparer = EqualityComparer<object>.Default;
            foreach (var value in values) sum += comparer.GetHashCode(value) + (comparer.Equals(value, values[0]) ? 1 : 0);
            """),
        ["MethodGroups"] = count => (Enums(count), $$"""
            object[] values = { {{List(count, index => $"E{index}.A")}} };
            foreach (var value in values)
            {
                Func<string?> text = value.ToString;
                Func<int> hash = value.GetHashCode;
                sum += text()!.Length + hash();
            }
            """),
        ["InterfaceDispatch"] = count => (Each(count, index => $"public sealed class C{index} : IShape {{ public int Area() => {index}; }}"), $$"""
            IShape[] shapes = { {{List(count, index => $"new C{index}()")}} };
            foreach (var shape in shapes) sum += shape.Area();
            """),
        ["TypeTests"] = count => (Each(count, index => $"public sealed class C{index} : IShape {{ public int Area() => {index}; }}"), $$"""
            object[] objects = { {{List(count, index => $"new C{index}()")}} };
            foreach (var value in objects) sum += (value is IShape ? 1 : 0) + (value is C0 ? 2 : 0) + ((value as IShape)?.Area() ?? 0);
            """),
        ["GenericVirtualMethods"] = count => (Each(count, index => $"public sealed class D{index} : Base {{ public override int Measure<T>() => {index} + typeof(T).Name.Length; }}"), $$"""
            Base[] bases = { {{List(count, index => $"new D{index}()")}} };
            foreach (var value in bases) sum += value.Measure<int>() + value.Measure<string>();
            """),
        ["StaticConstructors"] = count => (Each(count, index => $$"""
            public static class H{{index}}
            {
                public static object Value = new object();
                public static int Count;
                static H{{index}}() { Count = {{index}}; }
            }
            """), string.Join("\n", Enumerable.Range(0, count).Select(index => $"sum += H{index}.Count + (H{index}.Value is null ? 0 : 1);"))),
        ["DelegateTargets"] = count => (Each(count, index => $"public static class F{index} {{ public static int Apply(int value) => value + {index}; }}"), $$"""
            Func<int, int>[] functions = { {{List(count, index => $"F{index}.Apply")}} };
            foreach (var function in functions) sum += function(input);
            """),
        ["ClosedExtensionMethods"] = count => (Each(count, index => $"public static class X{index} {{ public static int Measure{index}(this string text) => text.Length + {index}; }}"), $$"""
            var text = "abc";
            Func<int>[] functions = { {{List(count, index => $"text.Measure{index}")}} };
            foreach (var function in functions) sum += function();
            """),
    };
}
