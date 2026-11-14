using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerAwareMembersCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void RectangularArraysAndInvocationListsMatchDesktop(bool optimize, WasmTarget target) =>
        Assert.Equal(42, Execute("RectangularArrayFixture", ArraySource, optimize, target));

    [Theory]
    [MemberData(nameof(Configurations))]
    public void ClassAndModuleConstructorsMatchDesktop(bool optimize, WasmTarget target) =>
        Assert.Equal(42, Execute("ClassConstructorFixture", ConstructorSource, optimize, target));

    // A RuntimeTypeHandle keeps all 32 bits of its type ID wherever it is stored: in an array, a struct field or a
    // box, and on its way to RunClassConstructor. The fixture has enough types that some IDs need more than a byte.
    [Theory]
    [InlineData(FinalWasmOptimization.O0, false)]
    [InlineData(FinalWasmOptimization.O0, true)]
    [InlineData(FinalWasmOptimization.Oz, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public void TypeHandlesKeepTheirWholeIdInMemory(FinalWasmOptimization optimization, bool stackTrace)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            $"TypeHandleWidthFixture_{optimization}_{stackTrace}", TypeHandleWidthSource,
            "TypeHandleWidthFixture.EntryPoint", optimization != FinalWasmOptimization.O0, WasmTarget.Wasm32, 41, [])
        {
            EmitStackTrace = stackTrace,
            Optimization = optimization,
        }));
    }

    private static readonly string TypeHandleWidthSource = $$"""
        using System;
        using System.Runtime.CompilerServices;
        namespace TypeHandleWidthFixture;

        public static class Registrar
        {
            public static int Count;
            public static int Register(int value) { Count += value; return 0; }
        }

        // No code reads these fields, so only RunClassConstructor runs the initializer.
        internal static class Registrations
        {
            static readonly int _first = Registrar.Register(1);
            static readonly int _second = Registrar.Register(2);
        }

        public struct Holder
        {
            public RuntimeTypeHandle Handle;
            public int After;
        }

        {{string.Concat(Enumerable.Range(0, 300).Select(index =>
            $"public sealed class Other{index} {{ public static readonly int Value = {index}; public int Field; }}\n"))}}

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var sum = 0;
                {{string.Concat(Enumerable.Range(0, 300).Select(index =>
                    $"sum += Other{index}.Value + new Other{index}().Field;\n"))}}
                if (sum != 44850) return 1;
                var handles = new[] { typeof(Other299).TypeHandle, typeof(Registrations).TypeHandle };
                if (Type.GetTypeFromHandle(handles[0]) != typeof(Other299) ||
                    handles[0].Value != typeof(Other299).TypeHandle.Value) return 2;
                var holder = new Holder { Handle = typeof(Other298).TypeHandle, After = -1 };
                if (Type.GetTypeFromHandle(holder.Handle) != typeof(Other298) || holder.After != -1) return 3;
                object boxed = typeof(Other297).TypeHandle;
                if (!((RuntimeTypeHandle)boxed).Equals(typeof(Other297).TypeHandle) ||
                    ((RuntimeTypeHandle)boxed).Equals(typeof(Other296).TypeHandle)) return 4;
                RuntimeHelpers.RunClassConstructor(handles[1]);
                return Registrar.Count == 3 ? input + 1 : 5;
            }
        }
        """;

    private static int Execute(string name, string source, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        return executor.Execute(new CompilationScenario(
            $"{name}_{optimize}_{target}", source, $"{name}.EntryPoint", optimize, target, 41, []));
    }

    internal const string ArraySource = """
        using System;
        namespace RectangularArrayFixture;

        public struct Point
        {
            public Point(int x, int y) { X = x; Y = y; }
            public int X;
            public int Y;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // Rank 2 and 3 arrays created from a runtime element type.
                var grid = (int[,])Array.CreateInstance(typeof(int), 2, 3);
                grid[1, 2] = 42;
                if (grid.Rank != 2 || grid.GetLength(0) != 2 || grid.GetLength(1) != 3 || grid.Length != 6) return 1;
                if ((int)grid.GetValue(1, 2)! != 42 || (int)grid.GetValue(1L, 2L)! != 42) return 2;
                if ((int)grid.GetValue(new[] { 1, 2 })! != 42 || (int)grid.GetValue(new long[] { 1, 2 })! != 42) return 3;
                var cube = (string[,,])Array.CreateInstance(typeof(string), 2, 2, 2);
                cube.SetValue("x", 1, 0, 1);
                if ((string)cube.GetValue(1, 0, 1)! != "x" || cube.GetValue(0, 0, 0) != null) return 4;
                if ((string)cube.GetValue(1L, 0L, 1L)! != "x" || cube[1, 0, 1] != "x") return 5;

                // Lengths arrays choose the rank; one length is a vector.
                if (Array.CreateInstance(typeof(double), new[] { 2, 2 }).GetType() != typeof(double[,])) return 6;
                var longs = Array.CreateInstance(typeof(byte), new long[] { 3, 1 });
                if (longs.GetType() != typeof(byte[,]) || longs.Length != 3) return 7;
                if (Array.CreateInstance(typeof(int), new[] { 4 }).GetType() != typeof(int[])) return 8;
                if (Array.CreateInstance(typeof(int), new[] { 4 }, new[] { 0 }).GetType() != typeof(int[])) return 9;
                if (Array.CreateInstance(typeof(int), new[] { 0, 3 }).Length != 0) return 10;

                // Lower bounds shift every index.
                var bounded = Array.CreateInstance(typeof(int), new[] { 2, 3 }, new[] { 1, -1 });
                if (bounded.GetType() != typeof(int[,])) return 11;
                if (bounded.GetLowerBound(0) != 1 || bounded.GetLowerBound(1) != -1 || bounded.GetUpperBound(1) != 1) return 12;
                bounded.SetValue(9, 2, 1);
                if ((int)bounded.GetValue(2, 1)! != 9) return 13;
                try { bounded.GetValue(0, 0); return 14; } catch (IndexOutOfRangeException) { }

                // Value elements box from rectangular storage.
                var points = Array.CreateInstance(typeof(Point), 2, 2);
                points.SetValue(new Point(3, 4), 1, 1);
                if (points.GetValue(1, 1) is not Point point || point.X != 3 || point.Y != 4) return 15;
                var nullable = new int?[2, 2];
                nullable[0, 1] = 5;
                if ((int)nullable.GetValue(0, 1)! != 5 || nullable.GetValue(1, 1) != null) return 16;

                // Argument validation follows upstream.
                try { Array.CreateInstance(null!, 1, 1); return 17; } catch (ArgumentNullException e) { if (e.ParamName != "elementType") return 18; }
                try { Array.CreateInstance(typeof(int), 1, -1); return 19; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "length2") return 20; }
                try { Array.CreateInstance(typeof(int), new int[0]); return 21; } catch (ArgumentException) { }
                try { Array.CreateInstance(typeof(int), new[] { 1, 2 }, new[] { 0 }); return 22; } catch (ArgumentException) { }
                try { Array.CreateInstance(typeof(int), new[] { 1, -2 }); return 23; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "lengths[1]") return 24; }
                try { Array.CreateInstance(typeof(int), new long[] { 1, long.MaxValue }); return 25; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "len") return 26; }
                try { Array.CreateInstance(typeof(int), new[] { 2, 2 }, new[] { 0, int.MaxValue }); return 27; } catch (ArgumentOutOfRangeException) { }
                try { Array.CreateInstance(typeof(int), (int[])null!); return 28; } catch (ArgumentNullException e) { if (e.ParamName != "lengths") return 29; }
                try { grid.GetValue(0); return 30; } catch (ArgumentException) { }
                try { grid.GetValue(0, 0, 0); return 31; } catch (ArgumentException) { }
                try { grid.GetValue(2, 0); return 32; } catch (IndexOutOfRangeException) { }
                try { grid.GetValue((int[])null!); return 33; } catch (ArgumentNullException e) { if (e.ParamName != "indices") return 34; }
                try { grid.GetValue(0L, long.MaxValue); return 35; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "index2") return 36; }
                try { cube.GetValue(0L, 0L, long.MinValue); return 37; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "index3") return 38; }
                try { new int[1].GetValue(0, 0); return 39; } catch (ArgumentException) { }

                // A multicast delegate lists its targets in invocation order.
                Func<int> first = () => 1;
                Func<int> second = () => 2;
                var combined = (MulticastDelegate)(first + second);
                var targets = combined.GetInvocationList();
                if (targets.Length != 2 || !targets[0].Equals(first) || !targets[1].Equals(second)) return 40;
                if (first.GetInvocationList().Length != 1) return 41;
                return input + 1;
            }
        }
        """;

    internal const string ConstructorSource = """
        using System;
        using System.Runtime.CompilerServices;
        namespace ClassConstructorFixture;

        public static class Log
        {
            public static int Explicit;
            public static int FieldOnly;
            public static int Generic;
        }

        public static class Explicit
        {
            public static int Value = 5;
            static Explicit() { Log.Explicit++; }
        }

        public static class FieldOnly
        {
            public static readonly int Value = Compute();
            private static int Compute() { Log.FieldOnly++; return 7; }
        }

        public class Generic<T>
        {
            public static int Value = 1;
            public static T[] Empty = new T[0];
            static Generic() { Log.Generic += 10; }
        }

        public static class Failing
        {
            public static int Value;
            static Failing() { throw new InvalidOperationException("initializer"); }
        }

        public static class Recursive
        {
            public static int Seen;
            static Recursive()
            {
                RuntimeHelpers.RunClassConstructor(typeof(Recursive).TypeHandle);
                Seen++;
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // An initializer runs once, whether started explicitly or by a static access.
                RuntimeHelpers.RunClassConstructor(typeof(Explicit).TypeHandle);
                if (Log.Explicit != 1) return 1;
                RuntimeHelpers.RunClassConstructor(typeof(Explicit).TypeHandle);
                if (Log.Explicit != 1 || Explicit.Value != 5) return 2;

                // Field-initializer-only (beforefieldinit) types run too.
                RuntimeHelpers.RunClassConstructor(typeof(FieldOnly).TypeHandle);
                if (Log.FieldOnly != 1) return 3;
                if (FieldOnly.Value != 7 || Log.FieldOnly != 1) return 4;

                // Each generic instantiation has its own initializer.
                RuntimeHelpers.RunClassConstructor(typeof(Generic<int>).TypeHandle);
                if (Log.Generic != 10) return 5;
                RuntimeHelpers.RunClassConstructor(typeof(Generic<string>).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(Generic<int>).TypeHandle);
                if (Log.Generic != 20 || Generic<string>.Value != 1) return 6;

                // A generic definition has no initializer of its own: only its instantiations run one.
                RuntimeHelpers.RunClassConstructor(typeof(Generic<>).TypeHandle);
                if (Log.Generic != 20 || Generic<int>.Empty.Length != 0) return 14;

                // Types without an initializer have nothing to run.
                RuntimeHelpers.RunClassConstructor(typeof(EntryPoint).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(int).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(string[]).TypeHandle);

                // A failed initializer stays failed. Desktop wraps the failure in
                // TypeInitializationException; NetWasm rethrows the original (docs/corelib-runtime.md).
                try { RuntimeHelpers.RunClassConstructor(typeof(Failing).TypeHandle); return 7; }
                catch (Exception e) when (IsInitializerFailure(e)) { }
                try { RuntimeHelpers.RunClassConstructor(typeof(Failing).TypeHandle); return 8; }
                catch (Exception e) when (IsInitializerFailure(e)) { }
                try { _ = Failing.Value; return 9; }
                catch (Exception e) when (IsInitializerFailure(e)) { }

                // An initializer that starts itself again sees it in progress.
                RuntimeHelpers.RunClassConstructor(typeof(Recursive).TypeHandle);
                if (Recursive.Seen != 1) return 10;

                // Handles must be initialized; no module handle names a module.
                try { RuntimeHelpers.RunClassConstructor(default); return 11; } catch (ArgumentException) { }
                try { RuntimeHelpers.RunModuleConstructor(default); return 12; } catch (ArgumentException) { }
                try { RuntimeHelpers.RunModuleConstructor(ModuleHandle.EmptyHandle); return 13; } catch (ArgumentException) { }
                return input + 1;
            }

            private static bool IsInitializerFailure(Exception e) =>
                (e is TypeInitializationException wrapper ? wrapper.InnerException : e) is
                    InvalidOperationException { Message: "initializer" };
        }
        """;
}
