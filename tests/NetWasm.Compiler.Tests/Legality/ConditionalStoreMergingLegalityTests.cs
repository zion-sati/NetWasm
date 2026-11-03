using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// Two arms that store one field become one store where they meet only when
// both store the same field of the same slot's object, nothing else reaches
// an arm, and each value is computed from loads and arithmetic. Each method
// breaks one condition: different fields, different objects, a condition
// whose two branches reach the second arm, a value that divides by zero
// while the object is null, a value that assigns the slot, narrowing stores
// to a small and a float field, arms that read the field through a copy of
// the object, different static fields, and a store in a try whose handler
// reads the field. Both arms storing one static field merge. So do arms
// whose values come from null, a static field, constants of every width,
// negation, complement, comparisons, conversions, another object's field, a
// remainder and shifts, and a generic class's field; an arm storing a
// volatile field or one that another field overlaps, arms storing a struct
// field, and arms whose values call a method, read an element, box, load a
// string, assign on the way or read through a field's address, do not.
public sealed class ConditionalStoreMergingLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private sealed class Holder { public int Count; public int Other; public sbyte Small; public float Ratio; }
            private static int s_a, s_b;

            // The arms store different fields.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DifferentFields(Holder holder, int input)
            {
                if (input % 2 == 0) holder.Count = input + 1; else holder.Other = input + 2;
                return holder.Count * 100 + holder.Other;
            }

            // The arms store the same field of different objects.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DifferentOwners(Holder first, Holder second, int input)
            {
                if (input % 2 == 0) first.Count = input + 3; else second.Count = input + 4;
                return first.Count * 100 + second.Count;
            }

            // Two branches of the condition reach the second arm.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int EitherCondition(Holder holder, int input)
            {
                if (input % 3 == 0 || input % 5 == 0) holder.Count = input * 7; else holder.Count = input * 11;
                return holder.Count;
            }

            // The value divides by zero on one arm while the object is null.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ThrowOrder(Holder? holder, int input, int divisor)
            {
                try
                {
                    if (input % 2 == 0) holder!.Count = input / divisor; else holder!.Count = input;
                    return holder.Count;
                }
                catch (DivideByZeroException) { return -10; }
                catch (NullReferenceException) { return -20; }
            }

            // The value assigns the slot the arm began by loading.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OwnerReassigned(Holder holder, Holder other, int input)
            {
                var original = holder;
                if (input % 2 == 0) holder.Count = (holder = other).Count + 5; else holder.Count = 9;
                return original.Count * 100 + other.Count;
            }

            // The arms narrow their values into a small and a float field.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Narrowing(Holder holder, int input)
            {
                if (input % 2 == 0) holder.Small = (sbyte)(input + 200); else holder.Small = (sbyte)(input - 200);
                if (input % 3 == 0) holder.Ratio = (float)(input * 1.1); else holder.Ratio = input;
                return holder.Small * 1000 + (int)(holder.Ratio * 10);
            }

            // The arms read the field they store through a copy of the object.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadModifyWrite(Holder holder, int input)
            {
                for (var i = 0; i < 5; i++)
                {
                    if ((input + i) % 2 == 0) holder.Count--; else holder.Count += 3;
                }
                return holder.Count;
            }

            // The arms store different static fields.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DifferentStatics(int input)
            {
                s_a = 0; s_b = 0;
                if (input % 2 == 0) s_a = input + 6; else s_b = input + 8;
                return s_a * 100 + s_b;
            }

            private sealed class Fields { public volatile int Flag; public Point Where; public object? Item; public bool Big; public int Count; }
            public struct Point { public int X, Y; }
            private static object? s_item = "item";

            // One arm stores a volatile field.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int VolatileArm(Fields fields, int input)
            {
                if (input % 2 == 0) fields.Count = input + 1; else fields.Flag = input + 2;
                return fields.Count * 100 + fields.Flag;
            }

            // Both arms store a struct field.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StructArms(Fields fields, Point first, Point second, int input)
            {
                if (input % 2 == 0) fields.Where = first; else fields.Where = second;
                return fields.Where.X * 10 + fields.Where.Y;
            }

            private sealed class Box<T>
            {
                public T? Value;

                // Both arms store a field of the type parameter's type.
                [MethodImpl(MethodImplOptions.NoInlining)]
                public void Set(bool first, T a, T b)
                {
                    if (first) Value = a; else Value = b;
                }
            }

            // Values from null, a static field, negation, complement, comparisons and conversions.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ValueShapes(Fields fields, int input)
            {
                if (input % 2 == 0) fields.Item = null; else fields.Item = s_item;
                if (input % 3 == 0) fields.Count = -input; else fields.Count = ~input;
                if (input % 4 == 0) fields.Big = input > 3; else fields.Big = input == 7;
                if (input % 5 == 0) fields.Count += (int)(input * 1.5); else fields.Count += (short)(input * 3000);
                return (fields.Item is null ? 1 : 2) * 1000000 + fields.Count * 10 + (fields.Big ? 1 : 0);
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
            private sealed class Overlay
            {
                [System.Runtime.InteropServices.FieldOffset(0)] public int Whole;
                [System.Runtime.InteropServices.FieldOffset(0)] public short Low;
            }

            // The second arm stores a field another field overlaps.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ExplicitArm(Fields fields, Overlay overlay, int input)
            {
                if (input % 2 == 0) fields.Count = input + 4; else overlay.Whole = 0x30000 + input;
                return fields.Count * 1000 + overlay.Low;
            }

            private sealed class Wide { public long Big; public float Ratio; public bool Flag; public bool Other; public int Count; }

            // Values from long and float constants, unsigned and less-than comparisons, another
            // object's field, a remainder and shifts.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int MoreValueShapes(Wide wide, Fields other, int input)
            {
                if (input % 2 == 0) wide.Big = 5000000000L; else wide.Big = input * 3L;
                if (input % 3 == 0) wide.Ratio = 1.5f; else wide.Ratio = input / 4f;
                if (input % 4 == 0) wide.Flag = input < 3; else wide.Flag = (uint)input > 5u;
                if (input % 5 == 0) wide.Other = (uint)input < 9u; else wide.Other = input >= 2;
                if (input % 6 == 0) wide.Count = other.Count % 7; else wide.Count = (input << 2) >> 1 ^ (int)((uint)input >> 3);
                return (int)(wide.Big % 1000) * 100000 + (int)(wide.Ratio * 4) * 1000 + (wide.Flag ? 100 : 0) + (wide.Other ? 10 : 0) + wide.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Twice(int value) => value * 2;

            // Arms whose values call a method, read an element and box.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArmsThatRunMore(Fields fields, int[] values, int input)
            {
                if (input % 2 == 0) fields.Count = Twice(input); else fields.Count = input + 3;
                if (input % 3 == 0) fields.Count += values[input % values.Length]; else fields.Count += 7;
                if (input % 4 == 0) fields.Item = input; else fields.Item = null;
                return fields.Count * 10 + (fields.Item is int boxed ? boxed : -1);
            }

            // Arms whose values load a string, assign a field or a local on the way, or read
            // a field through a struct field's address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArmsOutsideTheRules(Fields fields, Fields other, int input)
            {
                if (input % 2 == 0) fields.Item = "text"; else fields.Item = null;
                if (input % 3 == 0) fields.Count = other.Count = input + 1; else fields.Count = input;
                int local;
                if (input % 4 == 0) fields.Count += local = input * 2; else fields.Count += 5;
                if (input % 5 == 0) fields.Count += other.Where.X; else fields.Count += 1;
                return (fields.Item is string text ? text.Length : 0) * 10000 + fields.Count * 100 + other.Count;
            }

            // Both arms store one static field.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SameStatic(int input)
            {
                if (input % 2 == 0) s_a = input * 3; else s_a = input - 4;
                return s_a;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Throw(int input)
            {
                if (input >= 0) throw new InvalidOperationException();
            }

            // The store is in a try whose handler reads the field.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Handler(Holder holder, int input)
            {
                try
                {
                    if (input % 2 == 0) holder.Count = input + 12; else holder.Count = input + 13;
                    Throw(input);
                    return -1;
                }
                catch (InvalidOperationException) { return holder.Count; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int GenericArms(int input)
            {
                var numbers = new Box<int>();
                numbers.Set(input % 2 == 0, input * 7, input + 9);
                var texts = new Box<string>();
                texts.Set(input % 3 == 0, "abc", "de");
                return numbers.Value * 10 + texts.Value!.Length;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                return which switch
                {
                    0 => DifferentFields(new Holder(), input),
                    1 => DifferentOwners(new Holder(), new Holder(), input),
                    2 => EitherCondition(new Holder(), input),
                    3 => ThrowOrder(input % 4 < 2 ? null : new Holder(), input, input % 3),
                    4 => OwnerReassigned(new Holder { Count = 1 }, new Holder { Count = input }, input),
                    5 => Narrowing(new Holder(), input),
                    6 => ReadModifyWrite(new Holder { Count = input }, input),
                    7 => DifferentStatics(input),
                    8 => Handler(new Holder(), input),
                    9 => SameStatic(input),
                    10 => VolatileArm(new Fields(), input),
                    11 => StructArms(new Fields(), new Point { X = input, Y = 2 }, new Point { X = 3, Y = input }, input),
                    12 => GenericArms(input),
                    13 => ValueShapes(new Fields(), input),
                    14 => ExplicitArm(new Fields(), new Overlay { Whole = 0x10000 }, input),
                    15 => MoreValueShapes(new Wide(), new Fields { Count = input * 5 }, input),
                    16 => ArmsThatRunMore(new Fields(), [4, 9, 16], input),
                    17 => ArmsOutsideTheRules(new Fields(), new Fields { Where = new Point { X = 6, Y = 1 } }, input),
                    _ => -1,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 100), (1, 3), (2, 300), (3, 5), (5, 7), (6, 700), (100, 300), (101, 5), (102, 500), (103, 7), (105, 9), (106, 900), (200, 0), (201, 11), (202, 22), (203, 21), (205, 35), (206, 42), (300, -10), (301, -20), (302, 1), (303, 3), (305, -20), (306, -10), (400, 500), (401, 901), (402, 702), (403, 903), (405, 905), (406, 1106), (500, -56000), (501, 57010), (502, -53980), (503, 59033), (505, 61050), (506, -49934), (600, 3), (601, 8), (602, 5), (603, 10), (605, 12), (606, 9), (700, 600), (701, 9), (702, 800), (703, 11), (705, 13), (706, 1200), (800, 12), (801, 14), (802, 14), (803, 16), (805, 18), (806, 18), (900, 0), (901, -3), (902, 6), (903, -1), (905, 1), (906, 18), (1000, 100), (1001, 3), (1002, 300), (1003, 5), (1005, 7), (1006, 700), (1100, 2), (1101, 31), (1102, 22), (1103, 33), (1105, 35), (1106, 62), (1200, 3), (1201, 102), (1202, 142), (1203, 123), (1205, 142), (1206, 423), (1300, 1000000), (1301, 2029980), (1302, 1059970), (1303, 2089970), (1305, 2000010), (1306, 1179940), (1400, 4000), (1401, 1), (1402, 6000), (1403, 3), (1405, 5), (1406, 10000), (1500, 6110), (1501, 301002), (1502, 2014), (1503, 906016), (1505, 1505020), (1506, 6112), (1600, 40), (1601, 109), (1602, 109), (1603, 99), (1605, 149), (1606, 159), (1700, 40701), (1701, 700), (1702, 40800), (1703, 1004), (1705, 1600), (1706, 41307)];

    [Theory]
    [InlineData(ManagedConditionalStoreMerging.Auto)]
    [InlineData(ManagedConditionalStoreMerging.Enabled)]
    [InlineData(ManagedConditionalStoreMerging.Disabled)]
    public void MergesArmStoresOnlyWhenBothStoreOneFieldOfOneObject(ManagedConditionalStoreMerging merging) =>
        LegalityCases.Run($"ConditionalStoreMergingLegality{merging}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(ConditionalStoreMerging: merging));
}
