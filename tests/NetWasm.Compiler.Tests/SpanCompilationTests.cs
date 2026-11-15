// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class SpanCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void SpanAndMemoryConstructorsPreserveSafetyAndIdentity(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"SpanSafety_{target}_{optimize}",
            SpanSafetySource,
            "SpanSafetyFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            AllowUnsafe = true,
        });

        Assert.Equal(42, result);
    }

    [Fact]
    public void CompilerLowersSpanSlicingAndIndexing()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "SpanFixture",
            """
            namespace SpanFixture;

            public static class EntryPoint
            {
                public static int Length(int input)
                {
                    var values = new int[] { input, 1 };
                    System.Span<int> span = values;
                    return span.Length;
                }

                public static int Basic(int input)
                {
                    var values = new int[] { input, 1 };
                    System.Span<int> span = values;
                    return span[0] + span[1];
                }

                public static int Slice(int input)
                {
                    var values = new int[] { 0, input, 1 };
                    System.Span<int> span = values;
                    var slice = span.Slice(1, 2);
                    return slice[0] + slice[1];
                }

            }
            """);

        Assert.Equal(2, CompileAndRun("Length"));
        Assert.Equal(42, CompileAndRun("Basic"));
        Assert.Equal(42, CompileAndRun("Slice"));

        int CompileAndRun(string method)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "SpanFixture.EntryPoint",
                method,
                []));
            try
            {
                return ExecuteWithNode(result.ApplicationModule, assets.Directory, 41);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Span method {method} failed", exception);
            }
        }
    }

    [Fact]
    public void CompilerLowersStackallocToSpan()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "StackallocSpanFixture",
            """
            namespace StackallocSpanFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    System.Span<int> values = stackalloc int[2];
                    values[0] = input;
                    values[1] = 1;
                    return values[0] + values[1];
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StackallocSpanFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersMemoryAndReadOnlyMemoryViews()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "MemoryFixture",
            """
            namespace MemoryFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new int[] { 0, input, 1 };
                    var memory = new System.Memory<int>(values, 1, 2);
                    var span = memory.Span;
                    span[1]++;
                    var readOnly = new System.ReadOnlyMemory<int>(values, 1, 2);
                    return readOnly.Span[0] + readOnly.Span[1] - 1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "MemoryFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void SpanCopyHandlesOverlapAndValidatesRanges()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "SpanCopyFixture",
            """
            namespace SpanCopyFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new int[] { input, 1, 2, 3 };
                    var span = new System.Span<int>(values);
                    span.Slice(0, 3).CopyTo(span.Slice(1, 3));
                    return values[1] + values[2] - values[3];
                }

                public static int Defaults(int input)
                {
                    System.Span<int> span = default;
                    System.ReadOnlySpan<int> readOnly = default;
                    System.Memory<int> memory = default;
                    System.ReadOnlyMemory<int> readOnlyMemory = default;
                    return span.Length + readOnly.Length + memory.Length +
                        readOnlyMemory.Length + input;
                }

                public static int Bounds(int input)
                {
                    try
                    {
                        _ = new System.Span<int>(new int[1], input, 1);
                        return 0;
                    }
                    catch (System.ArgumentOutOfRangeException)
                    {
                        return 42;
                    }
                }
            }
            """);

        Assert.Equal(40, CompileAndRun("Run", 41));
        Assert.Equal(41, CompileAndRun("Defaults", 41));
        Assert.Equal(42, CompileAndRun("Bounds", 2));

        int CompileAndRun(string method, int input)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "SpanCopyFixture.EntryPoint",
                method,
                []));
            return ExecuteWithNode(result.ApplicationModule, assets.Directory, input);
        }
    }

    private const string SpanSafetySource = """
        namespace SpanSafetyFixture;

        public interface IMarker { }

        public class Base
        {
            public int Value;
        }

        public sealed class Derived : Base, IMarker { }

        public struct ReferenceHolder
        {
            public string? Text;
        }

        public struct PlainValue
        {
            public int Value;
        }

        public static unsafe class EntryPoint
        {
            public static int Run(int input)
            {
                if (!WritableExactArraysSucceed()) return -1;
                if (!WritableCovarianceIsRejected()) return -2;
                if (!CovariancePrecedesRangeValidation()) return -3;
                if (!ReadOnlyCovarianceSucceeds()) return -4;
                var nullRangeResult = NullRangesMatchTheRuntimeContract();
                if (nullRangeResult != 0) return nullRangeResult;
                if (!ArraySegmentDefaultsProduceEmptyViews()) return -6;
                if (!RangesRejectOverflow()) return -7;
                if (!PointerConstructorsProtectManagedReferences()) return -8;
                if (!SpanIdentityMatchesBackingStorage()) return -9;
                if (!ReadOnlySpanIdentityMatchesBackingStorage()) return -10;
                return input + 1;
            }

            private static bool WritableExactArraysSucceed()
            {
                var values = new Base[] { new Base(), new Base() };
                var span = new System.Span<Base>(values);
                span[0] = new Derived();
                var slice = new System.Span<Base>(values, 1, 1);
                slice[0] = new Derived();
                var memory = new System.Memory<Base>(values);
                memory.Span[0] = new Base();
                var memorySlice = new System.Memory<Base>(values, 1, 1);
                memorySlice.Span[0] = new Base();
                return values[0] != null && values[1] != null;
            }

            private static bool WritableCovarianceIsRejected()
            {
                Base[] covariant = new Derived[0];
                IMarker[] interfaceCovariant = new Derived[0];
                return RejectSpan(covariant) &&
                    RejectSpanSlice(covariant) &&
                    RejectMemory(covariant) &&
                    RejectMemorySlice(covariant) &&
                    RejectSpan(interfaceCovariant) &&
                    RejectMemory(interfaceCovariant);
            }

            private static bool RejectSpan<T>(T[] values)
            {
                try
                {
                    _ = new System.Span<T>(values);
                    return false;
                }
                catch (System.ArrayTypeMismatchException)
                {
                    return true;
                }
            }

            private static bool RejectSpanSlice<T>(T[] values)
            {
                try
                {
                    _ = new System.Span<T>(values, 0, 0);
                    return false;
                }
                catch (System.ArrayTypeMismatchException)
                {
                    return true;
                }
            }

            private static bool RejectMemory<T>(T[] values)
            {
                try
                {
                    _ = new System.Memory<T>(values);
                    return false;
                }
                catch (System.ArrayTypeMismatchException)
                {
                    return true;
                }
            }

            private static bool RejectMemorySlice<T>(T[] values)
            {
                try
                {
                    _ = new System.Memory<T>(values, 0, 0);
                    return false;
                }
                catch (System.ArrayTypeMismatchException)
                {
                    return true;
                }
            }

            private static bool CovariancePrecedesRangeValidation()
            {
                Base[] covariant = new Derived[0];
                try
                {
                    _ = new System.Span<Base>(covariant, -1, 1);
                    return false;
                }
                catch (System.ArrayTypeMismatchException)
                {
                }

                try
                {
                    _ = new System.Memory<Base>(covariant, -1, 1);
                    return false;
                }
                catch (System.ArrayTypeMismatchException)
                {
                    return true;
                }
            }

            private static bool ReadOnlyCovarianceSucceeds()
            {
                Base[] values = new Derived[] { new Derived { Value = 17 } };
                var span = new System.ReadOnlySpan<Base>(values);
                var slice = new System.ReadOnlySpan<Base>(values, 0, 1);
                var memory = new System.ReadOnlyMemory<Base>(values);
                var memorySlice = new System.ReadOnlyMemory<Base>(values, 0, 1);
                return span[0].Value == 17 && slice[0].Value == 17 &&
                    memory.Span[0].Value == 17 && memorySlice.Span[0].Value == 17;
            }

            private static int NullRangesMatchTheRuntimeContract()
            {
                int[]? values = null;
                var span = new System.Span<int>(values, 0, 0);
                var readOnlySpan = new System.ReadOnlySpan<int>(values, 0, 0);
                var memory = new System.Memory<int>(values, 0, 0);
                var readOnlyMemory = new System.ReadOnlyMemory<int>(values, 0, 0);
                if (span.Length != 0 || readOnlySpan.Length != 0 ||
                    memory.Length != 0 || readOnlyMemory.Length != 0)
                {
                    return -51;
                }

                if (!RejectNullSpan(values, 1, 0)) return -52;
                if (!RejectNullReadOnlySpan(values, 0, 1)) return -53;
                if (!RejectNullMemory(values, -1, 0)) return -54;
                if (!RejectNullReadOnlyMemory(values, 0, -1)) return -55;
                return 0;
            }

            private static bool RejectNullSpan(int[]? values, int start, int length)
            {
                try
                {
                    _ = new System.Span<int>(values, start, length);
                    return false;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectNullReadOnlySpan(int[]? values, int start, int length)
            {
                try
                {
                    var span = new System.ReadOnlySpan<int>(values, start, length);
                    return span.Length < 0;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectNullMemory(int[]? values, int start, int length)
            {
                try
                {
                    _ = new System.Memory<int>(values, start, length);
                    return false;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectNullReadOnlyMemory(int[]? values, int start, int length)
            {
                try
                {
                    _ = new System.ReadOnlyMemory<int>(values, start, length);
                    return false;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool ArraySegmentDefaultsProduceEmptyViews()
            {
                System.ArraySegment<int> segment = default;
                System.Span<int> span = segment;
                System.ReadOnlySpan<int> readOnlySpan = segment;
                System.Memory<int> memory = segment;
                System.ReadOnlyMemory<int> readOnlyMemory = segment;
                var values = new int[1];
                return span.Length == 0 && readOnlySpan.Length == 0 &&
                    memory.Length == 0 && readOnlyMemory.Length == 0 &&
                    new System.Span<int>(values, 1, 0).Length == 0 &&
                    new System.ReadOnlySpan<int>(values, 1, 0).Length == 0 &&
                    new System.Memory<int>(values, 1, 0).Length == 0 &&
                    new System.ReadOnlyMemory<int>(values, 1, 0).Length == 0;
            }

            private static bool RangesRejectOverflow()
            {
                var values = new int[1];
                return RejectSpanRange(values, int.MaxValue, 1) &&
                    RejectSpanRange(values, 0, int.MaxValue) &&
                    RejectReadOnlySpanRange(values, int.MaxValue, 1) &&
                    RejectReadOnlySpanRange(values, 0, int.MaxValue) &&
                    RejectMemoryRange(values, int.MaxValue, 1) &&
                    RejectMemoryRange(values, 0, int.MaxValue) &&
                    RejectReadOnlyMemoryRange(values, int.MaxValue, 1) &&
                    RejectReadOnlyMemoryRange(values, 0, int.MaxValue);
            }

            private static bool RejectSpanRange(int[] values, int start, int length)
            {
                try
                {
                    var span = new System.Span<int>(values, start, length);
                    return span.Length < 0;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectReadOnlySpanRange(
                int[] values,
                int start,
                int length)
            {
                try
                {
                    var span = new System.ReadOnlySpan<int>(values, start, length);
                    return span.Length < 0;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectMemoryRange(int[] values, int start, int length)
            {
                try
                {
                    var memory = new System.Memory<int>(values, start, length);
                    return memory.Length < 0;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectReadOnlyMemoryRange(
                int[] values,
                int start,
                int length)
            {
                try
                {
                    var memory = new System.ReadOnlyMemory<int>(values, start, length);
                    return memory.Length < 0;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool PointerConstructorsProtectManagedReferences()
            {
                if (!RejectReferenceSpan() || !RejectReferenceReadOnlySpan() ||
                    !RejectNestedReferenceSpan() ||
                    !RejectNestedReferenceReadOnlySpan() ||
                    !RejectNegativePrimitiveLength() ||
                    !RejectNegativePrimitiveReadOnlyLength())
                {
                    return false;
                }

                var value = new PlainValue { Value = 23 };
                var span = new System.Span<PlainValue>(&value, 1);
                var readOnly = new System.ReadOnlySpan<PlainValue>(&value, 1);
                var empty = new System.Span<int>((void*)0, 0);
                var readOnlyEmpty = new System.ReadOnlySpan<int>((void*)0, 0);
                return span[0].Value == 23 && readOnly[0].Value == 23 &&
                    empty.Length == 0 && readOnlyEmpty.Length == 0;
            }

            private static bool RejectReferenceSpan()
            {
                try
                {
                    var span = new System.Span<string>((void*)0, -1);
                    return span.Length != -1;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (System.ArgumentException)
                {
                    return true;
                }
            }

            private static bool RejectReferenceReadOnlySpan()
            {
                try
                {
                    var span = new System.ReadOnlySpan<string>((void*)0, -1);
                    return span.Length != -1;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (System.ArgumentException)
                {
                    return true;
                }
            }

            private static bool RejectNestedReferenceSpan()
            {
                try
                {
                    var span = new System.Span<ReferenceHolder>((void*)0, 0);
                    return span.Length < 0;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (System.ArgumentException)
                {
                    return true;
                }
            }

            private static bool RejectNestedReferenceReadOnlySpan()
            {
                try
                {
                    var span = new System.ReadOnlySpan<ReferenceHolder>((void*)0, 0);
                    return span.Length < 0;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (System.ArgumentException)
                {
                    return true;
                }
            }

            private static bool RejectNegativePrimitiveLength()
            {
                try
                {
                    var span = new System.Span<int>((void*)0, -1);
                    return span.Length != -1;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectNegativePrimitiveReadOnlyLength()
            {
                try
                {
                    var span = new System.ReadOnlySpan<int>((void*)0, -1);
                    return span.Length != -1;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool SpanIdentityMatchesBackingStorage()
            {
                var values = new[] { 1, 2 };
                var same = new System.Span<int>(values);
                var sameAgain = new System.Span<int>(values, 0, 2);
                var shorter = new System.Span<int>(values, 0, 1);
                var other = new System.Span<int>(new[] { 1, 2 });
                System.Span<int> firstDefault = default;
                System.Span<int> secondDefault = default;
                var arrayEmpty = new System.Span<int>(new int[0]);
                var firstEmptySlice = new System.Span<int>(values, 0, 0);
                var secondEmptySlice = new System.Span<int>(values, 1, 0);
                return same == sameAgain && !(same != sameAgain) &&
                    same != shorter && !(same == shorter) && same != other &&
                    firstDefault == secondDefault && firstDefault != arrayEmpty &&
                    firstEmptySlice != secondEmptySlice;
            }

            private static bool ReadOnlySpanIdentityMatchesBackingStorage()
            {
                var values = new[] { 1, 2 };
                var same = new System.ReadOnlySpan<int>(values);
                var sameAgain = new System.ReadOnlySpan<int>(values, 0, 2);
                var shorter = new System.ReadOnlySpan<int>(values, 0, 1);
                var other = new System.ReadOnlySpan<int>(new[] { 1, 2 });
                System.ReadOnlySpan<int> firstDefault = default;
                System.ReadOnlySpan<int> secondDefault = default;
                var arrayEmpty = new System.ReadOnlySpan<int>(new int[0]);
                var firstEmptySlice = new System.ReadOnlySpan<int>(values, 0, 0);
                var secondEmptySlice = new System.ReadOnlySpan<int>(values, 1, 0);
                return same == sameAgain && !(same != sameAgain) &&
                    same != shorter && !(same == shorter) && same != other &&
                    firstDefault == secondDefault && firstDefault != arrayEmpty &&
                    firstEmptySlice != secondEmptySlice;
            }
        }
        """;
}
