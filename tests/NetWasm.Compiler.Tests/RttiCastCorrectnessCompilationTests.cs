using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RttiCastCorrectnessCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesRuntimeCastAndArrayAssignmentSemantics(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimized
            ? assets.CompileOptimizedSource("RttiCastCorrectness", Source)
            : assets.CompileSource("RttiCastCorrectness", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RttiCastCorrectness.EntryPoint",
            "Run",
            [],
            Target: target));

        CompilerTestSupport.ValidateWithNode(
            compilation.ApplicationModule,
            assets.Directory);
        for (var input = 0; input < 27; input++)
        {
            Assert.Equal(
                42,
                CompilerTestSupport.ExecuteWithStandardWasiNode(
                    compilation.ApplicationModule,
                    assets.Directory,
                    input,
                    target,
                    compilation.StaticDataEnd));
        }
    }

    private const string Source = """
        #nullable enable
        using System;
        using System.Collections.Generic;

        namespace RttiCastCorrectness;

        public interface IItem { int Value { get; } }
        public sealed class Item : IItem { public int Value => 42; }
        public struct ValueItem : IItem { public int Value => 42; }
        public delegate T Producer<out T>();
        public enum Number : int { Answer = 42 }
        public enum OtherNumber : int { Answer = 42 }
        public interface IParent<out T> { T Get(); }
        public interface IChild<out T> : IParent<T> { }
        public sealed class Child : IChild<string> { public string Get() => "ok"; }
        public interface IBox<out T> { T Value { get; } }
        public sealed class Box<T> : IBox<T>
        {
            public Box(T value) { Value = value; }
            public T Value { get; }
        }
        public delegate void Consumer<in T>(T value);

        public static class EntryPoint
        {
            public static string Produce() => "ok";

            public static int Run(int input)
            {
                try
                {
                    switch (input)
                    {
                        case 0: { object[] a = new IItem[1]; a[0] = new Item(); return ((IItem)a[0]).Value; }
                        case 1: { object[] a = new object[1][]; a[0] = new string[1]; return 42; }
                        case 2: { object[] a = new Producer<object>[1]; a[0] = new Producer<string>(Produce); return ((Producer<object>)a[0])() is string ? 42 : -1; }
                        case 3: { object a = new string[1]; return a is IEnumerable<object> ? 42 : -1; }
                        case 4: { object a = new string[1]; return a is IList<object> ? 42 : -1; }
                        case 5: { object a = new int[1]; return a is uint[] ? 42 : -1; }
                        case 6: { object a = new Number[1]; return a is int[] ? 42 : -1; }
                        case 7: { object a = 42; int? b = a as int?; return b.GetValueOrDefault() == 42 ? 42 : -1; }
                        case 8: { object[] a = new IItem[1]; a[0] = new ValueItem(); return ((IItem)a[0]).Value; }
                        case 9: { object a = new Item(); try { var b = (string)a; return -1; } catch (InvalidCastException) { return 42; } }
                        case 10: { object? a = null; return a is IItem || (a as IItem) != null ? -1 : 42; }
                        case 11: { object a = new Producer<string>(Produce); return a is Producer<object> ? 42 : -1; }
                        case 12: { object a = new string[1]; return a is object[] ? 42 : -1; }
                        case 13: { object a = 42; try { long b = (long)a; return -1; } catch (InvalidCastException) { return 42; } }
                        case 14: { object a = new ValueItem[1]; return a is object[] ? -1 : 42; }
                        case 15: return ExerciseAllArrayInterfaces();
                        case 16: return ExerciseReducedArrayInterface();
                        case 17: { object a = new Producer<object>(() => new object()); return a is Producer<string> ? -1 : 42; }
                        case 18: { object a = new Producer<int>(() => 42); return a is Producer<object> ? -1 : 42; }
                        case 19: { object a = new Child(); return a is IParent<object> p && p.Get() is string ? 42 : -1; }
                        case 20: return NullableValueCases();
                        case 21: return NullableEnumAndGenericCases();
                        case 22: return ReducedArrayPairs();
                        case 23: return RejectSameSizeNonReducedArrays();
                        case 24: { object a = new nint[1]; return a is nuint[] ? 42 : -1; }
                        case 25: return CopyConstructedVariantInterfaceArray();
                        case 26: return StoreAndInvokeContravariantDelegate();
                        default: return -2;
                    }
                }
                catch (ArrayTypeMismatchException) { return -10; }
                catch (InvalidCastException) { return -11; }
                catch (NullReferenceException) { return -12; }
            }

            private static int ExerciseAllArrayInterfaces()
            {
                var values = new[] { "ok" };
                IEnumerable<object> enumerable = values;
                ICollection<object> collection = values;
                IList<object> list = values;
                IReadOnlyCollection<object> readOnlyCollection = values;
                IReadOnlyList<object> readOnlyList = values;
                var count = 0;
                foreach (var value in enumerable)
                {
                    if ((string)value != "ok") return -20;
                    count++;
                }
                var copy = new object[1];
                collection.CopyTo(copy, 0);
                list[0] = "again";
                return count == 1 && collection.Count == 1 &&
                    (string)copy[0] == "ok" && (string)list[0] == "again" &&
                    readOnlyCollection.Count == 1 &&
                    (string)readOnlyList[0] == "again" ? 42 : -21;
            }

            private static int ExerciseReducedArrayInterface()
            {
                var signed = new[] { -1 };
                IList<uint> values = (uint[])(object)signed;
                if (values[0] != uint.MaxValue) return -22;
                values[0] = 42;
                return signed[0] == 42 ? 42 : -23;
            }

            private static int NullableValueCases()
            {
                object boxed = new ValueItem();
                ValueItem? value = boxed as ValueItem?;
                int? empty = null;
                object? emptyBox = empty;
                object wrong = "wrong";
                return value.HasValue && value.Value.Value == 42 &&
                    emptyBox is null && (wrong as ValueItem?) is null ? 42 : -24;
            }

            private static int NullableEnumAndGenericCases()
            {
                object boxed = Number.Answer;
                Number? number = AsNullable<Number>(boxed);
                return number == Number.Answer ? 42 : -25;
            }

            private static T? AsNullable<T>(object value) where T : struct => value as T?;

            private static int ReducedArrayPairs()
            {
                object signedBytes = new sbyte[1];
                object unsignedShorts = new ushort[1];
                object signedLongs = new long[1];
                object firstEnum = new Number[1];
                return signedBytes is byte[] && unsignedShorts is short[] &&
                    signedLongs is ulong[] && firstEnum is OtherNumber[] ? 42 : -26;
            }

            private static int RejectSameSizeNonReducedArrays()
            {
                object floats = new float[1];
                object booleans = new bool[1];
                object characters = new char[1];
                return floats is int[] || booleans is byte[] || characters is ushort[]
                    ? -27
                    : 42;
            }

            private static int CopyConstructedVariantInterfaceArray()
            {
                IBox<string>[] source = [new Box<string>("ok")];
                IBox<object>[] destination = new IBox<object>[1];
                Array.Copy(source, destination, 1);
                return (string)destination[0].Value == "ok" ? 42 : -28;
            }

            private static int StoreAndInvokeContravariantDelegate()
            {
                var observed = 0;
                Consumer<object> consume = value =>
                {
                    if (value is string) observed = 42;
                };
                object[] values = new Consumer<string>[1];
                values[0] = consume;
                ((Consumer<string>)values[0])("ok");
                return observed;
            }
        }
        """;
}
