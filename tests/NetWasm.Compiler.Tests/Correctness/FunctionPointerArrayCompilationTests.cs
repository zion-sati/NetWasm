// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Arrays of function pointers, one- and two-dimensional, as desktop .NET has them: calls through their elements,
// copies and clones, their types' identity, the element type's facts (a function pointer, a class with no base type,
// unmanaged by its calling convention), the casts an array of them takes, GetValue's NotSupportedException and its
// siblings (IndexOf, the IList indexer, enumeration), pointers to function pointers, and a struct holding one compared by
// Equals; arrays of unmanaged pointers beside them. The types' names are the formatter's tests'.
public sealed class FunctionPointerArrayCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void FunctionPointerArraysBehaveAsDesktopDoes() =>
        runner.Run(new("FunctionPointerArrays", "FunctionPointerArrays", Source, [0, 1, 2, 7])
        {
            AllowUnsafe = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        namespace FunctionPointerArrays;

        public unsafe struct Holder
        {
            public delegate*<int, int> Function;
            public int Tag;
        }

        public static unsafe class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            private static int Add(int value) => value + 1;
            private static int Twice(int value) => value * 2;
            private static int Negate(int value) => -value;

            public static int Run(int input)
            {
                var hash = 17;
                void Mix(int value) => hash = unchecked(hash * 31 + value);
                void MixText(string? text)
                {
                    foreach (var character in text ?? "<null>") Mix(character);
                    Mix(-1);
                }

                var table = new delegate*<int, int>[] { &Add, &Twice, &Negate };
                Mix(table[input % 3](input + 5));
                foreach (var entry in table) Mix(entry(input));
                var copy = new delegate*<int, int>[3];
                Array.Copy(table, copy, 3);
                Mix(copy[2](input));
                var clone = (delegate*<int, int>[])table.Clone();
                Mix(clone[1](input));
                Mix(table.Length);

                object boxed = table;
                Mix(boxed is Array ? 1 : 0);
                Mix(boxed is IntPtr[] ? 1 : 0);
                Mix(boxed is object[] ? 1 : 0);
                Mix(boxed.GetType() == typeof(delegate*<int, int>[]) ? 1 : 0);
                var type = boxed.GetType();
                Mix(type.IsArray ? 1 : 0);

                var element = type.GetElementType()!;
                Mix(element.IsFunctionPointer ? 1 : 0);
                Mix(element.IsValueType ? 1 : 0);
                Mix(element.IsClass ? 1 : 0);
                Mix(element.IsPointer ? 1 : 0);
                Mix(element.IsPrimitive ? 1 : 0);
                Mix(element.BaseType is null ? 1 : 0);
                Mix(element.IsUnmanagedFunctionPointer ? 1 : 0);
                Mix(element.IsSealed ? 1 : 0);
                Mix(element.IsAbstract ? 1 : 0);
                Mix(element.HasElementType ? 1 : 0);
                Mix((int)Type.GetTypeCode(element));
                Mix(System.Runtime.InteropServices.Marshal.SizeOf(element) == IntPtr.Size ? 1 : 0);
                try
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(element);
                    Mix(1);
                }
                catch (ArgumentException exception)
                {
                    MixText(exception.Message);
                }
                var unmanaged = new delegate* unmanaged<int, int>[1];
                var native = unmanaged.GetType().GetElementType()!;
                Mix(native.IsFunctionPointer ? 1 : 0);
                Mix(native.IsUnmanagedFunctionPointer ? 1 : 0);
                try
                {
                    table.GetValue(0);
                    Mix(1);
                }
                catch (NotSupportedException)
                {
                    Mix(2);
                }

                var grid = new delegate*<int, int>[2, 2];
                grid[1, 0] = &Twice;
                grid[0, 1] = table[input % 3];
                Mix(grid[1, 0](input));
                Mix(grid[0, 1](input));
                Mix(grid.Rank);

                var values = stackalloc int[2];
                values[0] = input;
                values[1] = input + 1;
                var pointers = new int*[] { values, values + 1 };
                Mix(*pointers[1]);
                var pointer = pointers.GetType().GetElementType()!;
                Mix(pointer.IsPointer ? 1 : 0);
                Mix(pointer.IsFunctionPointer ? 1 : 0);
                Mix(pointer.IsValueType ? 1 : 0);

                // A pointer to a function pointer, and arrays of them.
                delegate*<int, int> slot = &Twice;
                delegate*<int, int>* indirect = &slot;
                Mix((*indirect)(input));
                var indirections = new delegate*<int, int>*[] { indirect };
                Mix((*indirections[0])(input + 1));
                Mix(typeof(delegate*<int, int>*).IsPointer ? 1 : 0);
                Mix(typeof(delegate*<int, int>*).GetElementType()!.IsFunctionPointer ? 1 : 0);
                Mix(typeof(delegate*<int, int>*).IsValueType ? 1 : 0);
                Mix(indirections.GetType().GetElementType()!.IsPointer ? 1 : 0);
                Mix(typeof(delegate*<int, int>) == table.GetType().GetElementType() ? 1 : 0);
                Mix(typeof(delegate*<int, int>) == typeof(delegate* unmanaged<int, int>) ? 1 : 0);
                Mix(sizeof(delegate*<int, int>) == IntPtr.Size ? 1 : 0);
                Mix(typeof(delegate*<int, int>).MakeArrayType() == typeof(delegate*<int, int>[]) ? 1 : 0);
                Mix(typeof(delegate*<int, int>[]).IsAssignableTo(typeof(System.Collections.IList)) ? 1 : 0);
                Mix(typeof(delegate*<int, int>).IsAssignableFrom(typeof(IntPtr)) ? 1 : 0);
                Mix(Array.CreateInstance(typeof(delegate*<int, int>), 2).Length);

                // Elements read as objects are not supported; clearing is.
                try
                {
                    Mix(Array.IndexOf(table, null));
                }
                catch (NotSupportedException)
                {
                    Mix(3);
                }
                try
                {
                    Mix(((System.Collections.IList)table)[0] is null ? 4 : 5);
                }
                catch (NotSupportedException)
                {
                    Mix(6);
                }
                try
                {
                    foreach (var item in (System.Collections.IEnumerable)table) Mix(7);
                }
                catch (NotSupportedException)
                {
                    Mix(8);
                }
                Array.Clear(clone);
                Mix(clone[0] == null ? 9 : 10);

                // A function pointer field compares as its bits in a struct's Equals.
                var holder = new Holder { Function = &Twice, Tag = input };
                Mix(holder.Function(input));
                object boxedHolder = holder;
                Mix(boxedHolder.Equals(new Holder { Function = &Twice, Tag = input }) ? 1 : 0);
                Mix(boxedHolder.Equals(new Holder { Function = &Add, Tag = input }) ? 1 : 0);
                Mix(boxedHolder.Equals(new Holder { Function = &Twice, Tag = input + 1 }) ? 1 : 0);
                // typeof the same types, which the compiler can answer itself.
                Mix(typeof(delegate*<int, int>).IsValueType ? 1 : 0);
                Mix(typeof(int*).IsValueType ? 1 : 0);
                Mix(typeof(delegate*<int, int>).IsFunctionPointer ? 1 : 0);
                Mix(typeof(int*).IsFunctionPointer ? 1 : 0);
                _trace = hash;
                return hash;
            }
        }
        """;
}
