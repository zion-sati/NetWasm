// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class NullableExpressionConversionTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void NullableConversionsAndEqualityMatchDesktop(bool optimized, WasmTarget target)
    {
        var expected = ExecuteDesktop();
        Assert.Equal(42, expected);
        using var assets = TestAssets.Create();
        var assembly = optimized
            ? assets.CompileOptimizedSource("NullableConversions", Source)
            : assets.CompileSource("NullableConversions", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "NullableConversions.EntryPoint", "Run", [], Target: target));
        Assert.Equal(expected, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 41, target, compilation.StaticDataEnd));
    }

    private static int ExecuteDesktop()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("NullableConversions.Desktop",
            [CSharpSyntaxTree.ParseText(Source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(compilation.Emit(stream).Success);
        stream.Position = 0;
        var context = new AssemblyLoadContext("NullableConversions.Desktop", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (int)assembly.GetType("NullableConversions.EntryPoint")!
                .GetMethod("Run")!.Invoke(null, [41])!;
        }
        finally
        {
            context.Unload();
        }
    }

    private const string Source = """
        using System;
        using System.Linq.Expressions;
        namespace NullableConversions;
        public sealed class Counter
        {
            public int Count;
            public int? Stored;
            public bool Throw;
            public int? NullableValue
            {
                get
                {
                    Count++;
                    if (Throw) throw new InvalidOperationException("getter");
                    return Stored;
                }
            }
            public int Next() { Count++; return Count; }
        }
        public enum Code : long { High = 1L << 40 }
        public class Model
        {
            public virtual long? Number => 1;
            public float? Single { get; set; }
            public double? Double { get; set; }
            public Code? Code { get; set; }
        }
        public sealed class Derived : Model
        {
            public long? Stored;
            public override long? Number => Stored;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Expression<Func<int, int?>> wrap = value => (int?)value;
                var wrapNode = (UnaryExpression)wrap.Body;
                if (!wrapNode.IsLifted || !wrapNode.IsLiftedToNull || wrap.Compile()(input) != input) return 1;

                Expression<Func<int?, int>> unwrap = value => (int)value;
                var unwrapNode = (UnaryExpression)unwrap.Body;
                var getValue = unwrap.Compile();
                if (!unwrapNode.IsLifted || unwrapNode.IsLiftedToNull || getValue(input) != input) return 2;
                try { getValue(null); return 3; }
                catch (InvalidOperationException) { }

                Expression<Func<int?, long?>> widen = value => (long?)value;
                var widenValue = widen.Compile();
                if (widenValue(null) != null || widenValue(input) != input) return 4;
                Expression<Func<int?, long>> unwrapWiden = value => (long)value;
                var unwrapWidenValue = unwrapWiden.Compile();
                if (unwrapWidenValue(input) != input) return 5;
                try { unwrapWidenValue(null); return 6; }
                catch (InvalidOperationException) { }

                Expression<Func<long?, short?>> narrow = value => (short?)value;
                if (narrow.Compile()(65535) != -1 || narrow.Compile()(null) != null) return 7;
                Expression<Func<bool, bool?>> wrapBool = value => (bool?)value;
                Expression<Func<bool?, bool>> unwrapBool = value => (bool)value;
                var getBool = unwrapBool.Compile();
                if (wrapBool.Compile()(true) != true || getBool(false)) return 8;
                try { getBool(null); return 9; }
                catch (InvalidOperationException) { }

                Expression<Func<int?, bool>> equal = value => value == input;
                var equals = equal.Compile();
                if (!equals(input) || equals(input + 1) || equals(null)) return 10;
                int? empty = null;
                Expression<Func<int?, bool>> nullEquality = value => value == empty;
                var capturedEquals = nullEquality.Compile();
                if (!capturedEquals(null) || capturedEquals(input)) return 11;
                empty = input;
                if (capturedEquals(null) || !capturedEquals(input)) return 25;

                var parameter = Expression.Parameter(typeof(int?), "value");
                var lifted = Expression.Equal(parameter,
                    Expression.Convert(Expression.Constant(input), typeof(int?)), true, null);
                var liftedEquals = Expression.Lambda<Func<int?, bool?>>(lifted, parameter).Compile();
                if (!lifted.IsLifted || !lifted.IsLiftedToNull || liftedEquals(null) != null ||
                    liftedEquals(input) != true || liftedEquals(input + 1) != false) return 12;

                var counter = new Counter();
                Expression<Func<int, int?>> once = unused => (int?)counter.Next();
                if (once.Compile()(input) != 1 || counter.Count != 1) return 13;

                Expression<Func<Counter, int?>> getterExpression = value => value.NullableValue;
                var getter = getterExpression.Compile();
                if (getter(counter) != null || counter.Count != 2) return 14;
                counter.Stored = input;
                if (getter(counter) != input || counter.Count != 3) return 15;
                counter.Throw = true;
                try { getter(counter); return 16; }
                catch (InvalidOperationException ex) { if (ex.Message != "getter") return 17; }
                if (counter.Count != 4) return 18;
                counter.Throw = false;
                try { throw new Exception("filter"); }
                catch (Exception) when (getter(counter) == input) { }
                if (counter.Count != 5) return 19;

                var model = new Derived();
                Expression<Func<Model, long?>> virtualExpression = value => value.Number;
                var virtualGetter = virtualExpression.Compile();
                if (virtualGetter(model) != null) return 20;
                model.Stored = 1L << 40;
                if (virtualGetter(model) != 1L << 40) return 21;
                Expression<Func<Model, float?>> singleExpression = value => value.Single;
                Expression<Func<Model, double?>> doubleExpression = value => value.Double;
                Expression<Func<Model, Code?>> enumExpression = value => value.Code;
                var singleGetter = singleExpression.Compile();
                var doubleGetter = doubleExpression.Compile();
                var enumGetter = enumExpression.Compile();
                if (singleGetter(model) != null || doubleGetter(model) != null || enumGetter(model) != null) return 22;
                model.Single = 1.25f;
                model.Double = 2.5;
                model.Code = Code.High;
                if (singleGetter(model) != 1.25f || doubleGetter(model) != 2.5 || enumGetter(model) != Code.High) return 23;
                try { getter(null); return 24; }
                catch (NullReferenceException) { }
                return 42;
            }
        }
        """;
}
