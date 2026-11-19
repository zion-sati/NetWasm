// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A type with an explicit static constructor (not BeforeFieldInit) initializes when one of its static methods or
// constructors is first invoked, as desktop .NET runs it, while its own static helpers are inlined into each other
// and its static methods check it once at their entry: before a side effect that precedes the first field access,
// when the invoked method touches no field, for each instantiation of a generic type separately, from a struct's
// constructor, and when the initializer throws, every time it is invoked (NetWasm rethrows the initializer's own
// exception where desktop wraps it in TypeInitializationException, a documented profile difference).
public sealed class PreciseInitializationCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void PreciseTypesInitializeWhenTheirMethodsAreInvoked() =>
        runner.Run(new("PreciseInitialization", "PreciseInitialization", Source, [0, 1, 3])
        {
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace PreciseInitialization;

        public static class Log
        {
            public static int Hash = 17;
            public static void Add(int value) => Hash = unchecked(Hash * 31 + value);
        }

        public static class Table
        {
            private static readonly int[] Values;
            private static int _reads;
            static Table()
            {
                Log.Add(100);
                Values = [3, 5, 7, 11];
            }

            private static int At(int index)
            {
                _reads++;
                return Values[index & 3];
            }

            private static int Twice(int index) => At(index) + At(index + 1);

            // Asks to be inlined, as do the other types' methods Run calls: only a method of the same type may inline
            // it without its type's trigger.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Sum(int input)
            {
                Log.Add(1);
                var sum = 0;
                for (var i = 0; i < 6; i++) sum += Twice(i + input);
                return sum + _reads;
            }
        }

        public static class Untouched
        {
            public static int Value = 9;
            static Untouched() => Log.Add(200);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Get(bool read) => read ? Value : -1;
        }

        public static class Failing
        {
            public static int Value = 1;
            static Failing()
            {
                Log.Add(300);
                throw new InvalidOperationException("initializer failed");
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Read() => Value;
        }

        public static class Generic<T>
        {
            public static int Count;
            static Generic() => Log.Add(Count + 400 + System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Next() => ++Count;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Other() => Generic<long>.Next() + Next();
        }

        public struct Point
        {
            public static int Made;
            public int X;
            static Point() => Log.Add(500);
            public Point(int x)
            {
                X = x;
                Made++;
            }
        }

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            public static int Run(int input)
            {
                Log.Add(input);
                Log.Add(Table.Sum(input));
                Log.Add(Table.Sum(input + 1));
                Log.Add(Untouched.Get(false));
                Log.Add(Untouched.Get(true));
                for (var i = 0; i < 2; i++)
                {
                    try
                    {
                        Log.Add(Failing.Read());
                    }
                    catch (Exception exception)
                    {
                        // NetWasm rethrows the initializer's own exception where desktop wraps it.
                        var failure = exception is TypeInitializationException { InnerException: { } inner } ? inner : exception;
                        Log.Add(failure is InvalidOperationException { Message: "initializer failed" } ? 7 : 8);
                    }
                }
                Log.Add(Generic<int>.Other());
                Log.Add(Generic<int>.Next());
                Log.Add(Generic<long>.Next());
                Log.Add(new Point(input).X + Point.Made);
                _trace = Log.Hash;
                return Log.Hash;
            }
        }
        """;
}
