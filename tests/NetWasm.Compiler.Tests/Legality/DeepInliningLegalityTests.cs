using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// Inlining, through the rounds past the general depth that take only small
// callees, changes nothing a program can see. Each chain is six calls deep
// and its bottom does one thing inlining must keep: arguments run once each
// and in order, a callee writes its parameter, a callee writes through a ref
// that aliases another argument's source, a finally runs as a callee's
// exception leaves it, a small callee recurses, a virtual call goes through a
// base-typed reference, a precise static constructor runs only when the
// callee reads its field, NoInlining stops a chain, an argument's slot is
// stored after its load and before the call, and a null receiver throws
// before the callee's first effect.
public sealed class DeepInliningLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private const MethodImplOptions Inline = MethodImplOptions.AggressiveInlining;
            private static int s_order;
            private static int s_initialized;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Next(int value) { s_order = s_order * 10 + value; return value; }

            // Arguments run once each, in order, though the callee reads them twice.
            [MethodImpl(Inline)] private static int Twice(int a, int b) => a * 100 + b + a * b;
            [MethodImpl(Inline)] private static int Order5(int a, int b) => Twice(a, b);
            [MethodImpl(Inline)] private static int Order4(int a, int b) => Order5(a, b);
            [MethodImpl(Inline)] private static int Order3(int a, int b) => Order4(a, b);
            [MethodImpl(Inline)] private static int Order2(int a, int b) => Order3(a, b);
            [MethodImpl(Inline)] private static int Order1(int a, int b) => Order2(a, b);

            // The callee writes its parameter; the caller's local keeps its value.
            [MethodImpl(Inline)] private static int Bump(int value) { value += 5; return value; }
            [MethodImpl(Inline)] private static int Param5(int value) => Bump(value);
            [MethodImpl(Inline)] private static int Param4(int value) => Param5(value);
            [MethodImpl(Inline)] private static int Param3(int value) => Param4(value);
            [MethodImpl(Inline)] private static int Param2(int value) => Param3(value);
            [MethodImpl(Inline)] private static int Param1(int value) => Param2(value);

            // The callee writes through a ref that aliases another argument's source.
            [MethodImpl(Inline)] private static int Alias(ref int target, int copy) { target += 3; return copy * 10 + target; }
            [MethodImpl(Inline)] private static int Ref5(ref int target, int copy) => Alias(ref target, copy);
            [MethodImpl(Inline)] private static int Ref4(ref int target, int copy) => Ref5(ref target, copy);
            [MethodImpl(Inline)] private static int Ref3(ref int target, int copy) => Ref4(ref target, copy);
            [MethodImpl(Inline)] private static int Ref2(ref int target, int copy) => Ref3(ref target, copy);
            [MethodImpl(Inline)] private static int Ref1(ref int target, int copy) => Ref2(ref target, copy);

            // A finally runs as the callee's exception leaves it.
            [MethodImpl(Inline)] private static int Throws(int value)
            {
                try { if (value % 2 == 0) throw new InvalidOperationException(); return value; }
                finally { s_order += 1000; }
            }
            [MethodImpl(Inline)] private static int Throw5(int value) => Throws(value);
            [MethodImpl(Inline)] private static int Throw4(int value) => Throw5(value);
            [MethodImpl(Inline)] private static int Throw3(int value) => Throw4(value);
            [MethodImpl(Inline)] private static int Throw2(int value) => Throw3(value);
            [MethodImpl(Inline)] private static int Throw1(int value) => Throw2(value);

            // A small recursive callee.
            [MethodImpl(Inline)] private static int Count(int value) => value <= 0 ? 0 : 1 + Count(value - 2);
            [MethodImpl(Inline)] private static int Recurse2(int value) => Count(value);
            [MethodImpl(Inline)] private static int Recurse1(int value) => Recurse2(value);

            private class Shape { public virtual int Area(int side) => side * side; }
            private sealed class Triangle : Shape { public override int Area(int side) => side * side / 2; }

            // A virtual call through a base-typed reference at the bottom.
            [MethodImpl(Inline)] private static int Area(Shape shape, int side) => shape.Area(side);
            [MethodImpl(Inline)] private static int Virtual4(Shape shape, int side) => Area(shape, side);
            [MethodImpl(Inline)] private static int Virtual3(Shape shape, int side) => Virtual4(shape, side);
            [MethodImpl(Inline)] private static int Virtual2(Shape shape, int side) => Virtual3(shape, side);
            [MethodImpl(Inline)] private static int Virtual1(Shape shape, int side) => Virtual2(shape, side);

            private static class Precise
            {
                public static int Value;
                static Precise() { s_initialized += 1; Value = 40; }
            }

            // A precise static constructor runs when the callee reads the field, not before.
            [MethodImpl(Inline)] private static int ReadPrecise(int value) => value > 0 ? Precise.Value : -1;
            [MethodImpl(Inline)] private static int Init4(int value) => ReadPrecise(value);
            [MethodImpl(Inline)] private static int Init3(int value) => Init4(value);
            [MethodImpl(Inline)] private static int Init2(int value) => Init3(value);
            [MethodImpl(Inline)] private static int Init1(int value) => Init2(value);

            // NoInlining stops the chain.
            [MethodImpl(MethodImplOptions.NoInlining)] private static int Kept(int value) => value * 3 + 1;
            [MethodImpl(Inline)] private static int Stop3(int value) => Kept(value);
            [MethodImpl(Inline)] private static int Stop2(int value) => Stop3(value) + Stop3(value + 1);
            [MethodImpl(Inline)] private static int Stop1(int value) => Stop2(value);

            private sealed class Worker
            {
                public int Field = 3;

                // The callee's first act is an effect; its receiver is read after it.
                [MethodImpl(Inline)] public int Work(int value) { Next(value); return Field + value; }
            }

            [MethodImpl(Inline)] private static int Work3(Worker? worker, int value) => worker!.Work(value);
            [MethodImpl(Inline)] private static int Work2(Worker? worker, int value) => Work3(worker, value);
            [MethodImpl(Inline)] private static int Work1(Worker? worker, int value) => Work2(worker, value);

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                s_order = 0;
                switch (which)
                {
                    case 0: { var result = Order1(Next(input + 1), Next(input + 2)); return result * 1000 + s_order % 1000; }
                    case 1: { var local = input; var bumped = Param1(local); return bumped * 100 + local; }
                    case 2: { var target = input; var result = Ref1(ref target, target); return result * 100 + target; }
                    case 3:
                        try { return Throw1(input) + s_order; }
                        catch (InvalidOperationException) { return -s_order; }
                    case 4: return Recurse1(input + 7);
                    case 5: return Virtual1(input % 2 == 0 ? new Triangle() : new Shape(), input + 3);
                    case 6:
                        {
                            var before = s_initialized;
                            var value = Init1(input);
                            return before * 1000 + s_initialized * 100 + value;
                        }
                    case 7: return Stop1(input);
                    case 8: { var x = input; var result = Order1(x, x = input + 9); return result * 100 + x; }
                    case 9:
                        try { return Work1(input % 2 == 0 ? null : new Worker(), input + 1) * 1000 + s_order; }
                        catch (NullReferenceException) { return -1000 - s_order; }
                    default: return -9;
                }
            }
        }

        """;

    // As desktop .NET 11 computes them, each input in a fresh process.
    private static readonly (int Input, int Expected)[] Cases = [(0, 104012), (1, 209023), (2, 316034), (5, 649067), (100, 500), (101, 601), (102, 702), (105, 1005), (200, 303), (201, 1404), (202, 2505), (205, 5808), (300, -1000), (301, 1001), (302, -1000), (305, 1005), (400, 4), (401, 4), (402, 5), (405, 6), (500, 4), (501, 16), (502, 12), (505, 64), (600, -1), (601, 140), (602, 140), (605, 140), (700, 5), (701, 11), (702, 17), (705, 35), (800, 909), (801, 12010), (802, 23311), (805, 58414), (900, -1000), (901, 5002), (902, -1000), (905, 9006)];

    public static TheoryData<string> Settings() => ["Auto", "NoInlining", "IgnoreAggressive"];

    [Theory]
    [MemberData(nameof(Settings))]
    public void InliningChangesNothingAProgramCanSee(string setting) =>
        LegalityCases.Run($"DeepInliningLegality{setting}", Source, Cases, FinalWasmOptimization.O3, setting switch
        {
            "NoInlining" => new WasmOptimizationOverrides(ManagedMethodInlining: ManagedMethodInlining.Disabled),
            "IgnoreAggressive" => new WasmOptimizationOverrides(ManagedInliningHints: ManagedInliningHints.IgnoreAggressive),
            _ => null,
        });
}
