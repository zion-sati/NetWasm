using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayCreateInstanceContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RuntimeElementAllocationMatchesDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ArrayCreationFixture", Source, "ArrayCreationFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None, AllowUnsafe = true }));
    }

    // A vector type reached only through a signature, whose element type is never constructed, has no
    // element type id: runtime array creation leaves it out instead of failing for its missing layout.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void VectorsOfNeverConstructedElementsDoNotNeedALayout(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "UnconstructedVectorFixture", UnconstructedVectorSource, "UnconstructedVectorFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string UnconstructedVectorSource = """
        using System;
        namespace UnconstructedVectorFixture;
        public sealed class Never { public int Value; }
        public static class EntryPoint
        {
            public static int Count(Never[] items) => items?.Length ?? -1;
            public static int Run(int input)
            {
                var ints = (int[])Array.CreateInstance(typeof(int), 2);
                return Count(null) == -1 && ints.Length == 2 ? input + 1 : 0;
            }
        }
        """;

    internal const string Source = """
        #nullable disable
        using System;
        namespace ArrayCreationFixture;
        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                var refs = Array.CreateInstance(typeof(Item), 2);
                refs.SetValue(new Item { Value = 42 }, 1);
                if (refs.Length != 2 || refs.GetValue(0) != null || ((Item)refs.GetValue(1)).Value != 42) return 1;
                if (refs is not Item[]) return 2;
                var ints = Array.CreateInstance(typeof(int), 3); ints.SetValue(42, 1);
                if (ints is not int[] || (int)ints.GetValue(0) != 0 || (int)ints.GetValue(1) != 42) return 3;
                var nullable = Array.CreateInstance(typeof(int?), 2); nullable.SetValue(42, 1);
                if (nullable is not int?[]) return 31;
                if (nullable.GetValue(0) != null) return 32;
                if ((int)nullable.GetValue(1) != 42) return 33;
                var structs = Array.CreateInstance(typeof(Pair), 1); structs.SetValue(new Pair { Text = "alive", Value = 42 }, 0);
                GC.Collect(); var pair = (Pair)structs.GetValue(0);
                if (pair.Text != "alive" || pair.Value != 42) return 5;
                var iface = Array.CreateInstance(typeof(IMarker), 1); iface.SetValue(new Item(), 0);
                if (iface is not IMarker[] || iface.GetValue(0) is not Item) return 6;
                var abstractArray = Array.CreateInstance(typeof(Base), 0); if (abstractArray.Length != 0 || abstractArray is not Base[]) return 7;
                var jagged = Array.CreateInstance(typeof(int[]), 1); jagged.SetValue(new int[] { 42 }, 0);
                if (jagged is not int[][] || ((int[])jagged.GetValue(0))[0] != 42) return 8;
                var selected = input == 41 ? typeof(int) : typeof(Item);
                if (Array.CreateInstance(selected, 0).Length != 0) return 9;
                try { Array.CreateInstance(null, -1); return 10; } catch (ArgumentNullException e) { if (e.ParamName != "elementType") return 11; }
                try { Array.CreateInstance(typeof(int), -1); return 12; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "length") return 13; }
                try { Array.CreateInstance(typeof(void), 1); return 14; } catch (NotSupportedException) { }
                try { Array.CreateInstance(typeof(Generic<>), 1); return 15; } catch (NotSupportedException) { }
                var pointers = Array.CreateInstance(typeof(int*), 2);
                if (pointers is not int*[] || pointers.Length != 2) return 16;
                try { pointers.GetValue(0); return 19; } catch (NotSupportedException) { }
                var voidPointers = Array.CreateInstance(typeof(void*), 0);
                if (voidPointers.Length != 0) return 17;
                try { Array.CreateInstance(typeof(Span<int>), 1); return 18; } catch (NotSupportedException) { }
                return input + 1;
            }
        }
        public interface IMarker { }
        public abstract class Base { }
        public sealed class Item : Base, IMarker { public int Value; }
        public sealed class Generic<T> { }
        public struct Pair { public string Text; public int Value; }
        """;
}
