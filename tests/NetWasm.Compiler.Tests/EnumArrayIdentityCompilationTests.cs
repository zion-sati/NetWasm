using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumArrayIdentityCompilationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DesktopPreservesTypeBasedEnumArrayIdentityAndBoxedElements(int scenario)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("EnumArrayIdentity.Desktop",
            [CSharpSyntaxTree.ParseText(scenario switch
            {
                0 => Source,
                1 => GenericSource,
                _ => ImplicitArraySource,
            }, cancellationToken: TestContext.Current.CancellationToken)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success);
        stream.Position = 0;
        var context = new AssemblyLoadContext("EnumArrayIdentity.Desktop", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            Assert.Equal(42, assembly.GetType("EntryPoint")!.GetMethod("Run")!.Invoke(null, [0]));
        }
        finally
        {
            context.Unload();
        }
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void TypeBasedEnumArraysPreserveIdentityAndBoxedElements(bool optimized, WasmTarget target)
        => Execute(Source, optimized, target);

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void NestedGenericEnumsPreserveClosedIdentitiesAndRejectOpenAllocations(bool optimized, WasmTarget target)
        => Execute(GenericSource, optimized, target);

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void TypeBasedEnumEnumerationRetainsImplicitArraysAndBoxes(bool optimized, WasmTarget target)
        => Execute(ImplicitArraySource, optimized, target);

    private static void Execute(string source, bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "EnumArrayIdentity", source, "EntryPoint", optimized, target, 0, [])));
    }

    private const string Source = """
        using System;
        [Flags] public enum Permissions { None = 0, Read = 1, Write = 2 }
        public enum Wide : long { None = 0, High = 1L << 40 }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = Enum.GetValues(typeof(Permissions));
                if (values is object[]) return 1;
                if (values.GetType() != typeof(Permissions[])) return 2;
                var sum = 0L;
                foreach (var value in values)
                {
                    if (value.GetType() != typeof(Permissions)) return 3;
                    sum += Convert.ToInt64(value);
                }
                if (sum != 3) return 4;
                var wideValues = Enum.GetValues(typeof(Wide));
                if (wideValues is object[] || wideValues.GetType() != typeof(Wide[])) return 5;
                sum = 0;
                foreach (var value in wideValues)
                {
                    if (value.GetType() != typeof(Wide)) return 6;
                    sum += Convert.ToInt64(value);
                }
                return sum == 1L << 40 ? 42 : 7;
            }
        }
        """;

    private const string GenericSource = """
        using System;
        public class Outer<T> { public enum Code : ushort { Seven = 7 } }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var left = Enum.GetValues(typeof(Outer<int>.Code));
                var right = Enum.GetValues(typeof(Outer<string>.Code));
                if (left.GetType() == right.GetType() ||
                    left.GetType() != typeof(Outer<int>.Code[]) ||
                    right.GetType() != typeof(Outer<string>.Code[])) return 1;
                var leftValue = left.GetValue(0);
                var rightValue = right.GetValue(0);
                if (leftValue.GetType() != typeof(Outer<int>.Code) ||
                    rightValue.GetType() != typeof(Outer<string>.Code) ||
                    Convert.ToInt64(leftValue) != 7 || Convert.ToInt64(rightValue) != 7) return 2;
                if (Enum.GetValues<Outer<int>.Code>()[0] != Outer<int>.Code.Seven ||
                    Enum.GetValues<Outer<string>.Code>()[0] != Outer<string>.Code.Seven) return 3;
                if (Enum.GetNames<Outer<int>.Code>()[0] != "Seven" ||
                    Enum.GetName(Outer<string>.Code.Seven) != "Seven" ||
                    !Enum.IsDefined(Outer<int>.Code.Seven)) return 4;
                var boxed = Enum.ToObject(typeof(Outer<int>.Code), (ushort)7);
                if (boxed.GetType() != typeof(Outer<int>.Code) ||
                    (Outer<int>.Code)boxed != Outer<int>.Code.Seven) return 5;
                try { Enum.ToObject(typeof(Outer<>.Code), (ushort)7); return 6; }
                catch (ArgumentException) { }
                try { Enum.GetValues(typeof(Outer<>.Code)); return 7; }
                catch (NotSupportedException) { }
                var numeric = Enum.GetValuesAsUnderlyingType(typeof(Outer<>.Code));
                if (numeric.GetType() != typeof(ushort[]) || (ushort)numeric.GetValue(0) != 7) return 8;
                return 42;
            }
        }
        """;

    // No enum-array type token or allocation may supply the runtime roots here.
    // Enum.GetValues and Array enumeration must retain their own arrays and boxes.
    private const string ImplicitArraySource = """
        using System;
        public enum Code : ushort { Seven = 7, Nine = 9 }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = Enum.GetValues(typeof(Code));
                if (values is object[]) return 1;
                var count = 0;
                var sum = 0L;
                foreach (var value in values)
                {
                    if (value.GetType() != typeof(Code)) return 2;
                    sum += Convert.ToInt64(value);
                    count++;
                }
                return count == 2 && sum == 16 ? 42 : 3;
            }
        }
        """;
}
