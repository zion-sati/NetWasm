using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A field read takes an earlier read's value only while nothing can have
// changed the field. Each method breaks one condition that must stop it: the
// same field written through another reference to the object, the owner
// local and the owner argument reassigned, a call that writes the field, a store through a ref
// parameter or a returned ref, another type's explicit static constructor
// writing the static, a store on one branch before the merge, a write inside
// a loop, Interlocked on the field, an explicit-layout field that overlaps
// it, the owner's own field replaced, and a write in a try that its handler
// reads again.
public sealed class FieldLoadReuseLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Threading;

        public static class EntryPoint
        {
            private sealed class Holder { public int Count; public int Other; public Holder? Next; }

            [StructLayout(LayoutKind.Explicit)]
            private sealed class Overlay { [FieldOffset(0)] public int Whole; [FieldOffset(0)] public short Low; }

            private static class Source { public static int Value = 3; }
            // An explicit static constructor, so it runs exactly at the first access.
            private static class Writer
            {
                public static int Marker;
                static Writer() { Source.Value = 40; Marker = 1; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(Holder holder) => holder.Count += 5;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static ref int CountOf(Holder holder) => ref holder.Count;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ThroughAlias(Holder first, Holder second)
            {
                var a = first.Count;
                second.Count = a + 7;
                return a * 100 + first.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OwnerReassigned(Holder first, Holder second)
            {
                var holder = first;
                var a = holder.Count;
                holder = second;
                return a * 100 + holder.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArgumentReassigned(Holder holder, Holder second)
            {
                var a = holder.Count;
                holder = second;
                return a * 100 + holder.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CallWrites(Holder holder)
            {
                var a = holder.Count;
                Bump(holder);
                return a * 100 + holder.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RefParameter(ref int count, Holder holder)
            {
                var a = holder.Count;
                count = a + 11;
                return a * 100 + holder.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReturnedRef(Holder holder)
            {
                ref var count = ref CountOf(holder);
                var a = holder.Count;
                count = a + 13;
                return a * 100 + holder.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StaticInitializer()
            {
                var a = Source.Value;
                var marker = Writer.Marker;
                return a * 100 + Source.Value + marker;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int BranchStore(Holder holder, int input)
            {
                var a = holder.Count;
                if (input % 2 == 1) holder.Count = a + 17;
                return a * 100 + holder.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LoopWrites(Holder holder, int input)
            {
                var total = 0;
                for (var i = 0; i < 4; i++)
                {
                    total = total * 3 + holder.Count;
                    if (i == input % 4) Bump(holder);
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Interlocked(Holder holder)
            {
                var a = holder.Count;
                System.Threading.Interlocked.Add(ref holder.Count, 19);
                return a * 100 + holder.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Overlapping(Overlay overlay, int input)
            {
                var a = overlay.Whole;
                overlay.Low = (short)(input + 2);
                return a * 1000 + overlay.Whole;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ChainedOwner(Holder holder)
            {
                var a = holder.Next!.Count;
                holder.Next = new Holder { Count = a + 23 };
                return a * 100 + holder.Next.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Throw(int input)
            {
                if (input >= 0) throw new InvalidOperationException();
            }

            // The field is written in a try and read again in its handler.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Handler(Holder holder, int input)
            {
                var a = holder.Count;
                try
                {
                    holder.Count = a + 29;
                    Throw(input);
                    return -1;
                }
                catch (InvalidOperationException)
                {
                    return a * 100 + holder.Count;
                }
            }

            public static int Run(int input)
            {
                var first = new Holder { Count = input };
                var result = ThroughAlias(first, first);
                result = result * 7 + OwnerReassigned(new Holder { Count = input }, new Holder { Count = input + 1 });
                result = result * 7 + CallWrites(new Holder { Count = input });
                var shared = new Holder { Count = input };
                result = result * 7 + RefParameter(ref shared.Count, shared);
                result = result * 7 + ReturnedRef(new Holder { Count = input });
                result = result * 7 + (input == 0 ? StaticInitializer() : 0);
                result = result * 7 + BranchStore(new Holder { Count = input }, input);
                result = result * 7 + LoopWrites(new Holder { Count = input }, input);
                result = result * 7 + Interlocked(new Holder { Count = input });
                result = result * 7 + Overlapping(new Overlay { Whole = 0x10000 * (input + 1) }, input);
                result = result * 7 + ChainedOwner(new Holder { Next = new Holder { Count = input } });
                result = result * 7 + Handler(new Holder { Count = input }, input);
                result = result * 7 + ArgumentReassigned(new Holder { Count = input }, new Holder { Count = input + 2 });
                return result;
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, -973210996), (1, -1433106422), (2, -1615669541), (3, -1794064524), (6, 1958053831)];

    [Theory]
    [InlineData(ManagedFieldLoadReuse.Auto)]
    [InlineData(ManagedFieldLoadReuse.Enabled)]
    [InlineData(ManagedFieldLoadReuse.Disabled)]
    public void ReusesAFieldReadOnlyWhileNothingCanChangeTheField(ManagedFieldLoadReuse reuse) =>
        LegalityCases.Run($"FieldLoadReuseLegality{reuse}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(FieldLoadReuse: reuse));
}
