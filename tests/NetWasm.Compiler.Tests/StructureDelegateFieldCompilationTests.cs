// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A non-null delegate field, which NetWasm does not convert to a function pointer, throws PlatformNotSupportedException
// before any memory access: StructureToPtr leaves the native structure as it was and, with fDeleteOld, frees none of
// its old strings; PtrToStructure leaves the structure it fills as it was; and the same holds for a delegate in an
// embedded class or a later element of an array of structures. A null delegate converts to a null pointer.
public sealed class StructureDelegateFieldCompilationTests
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
    public void ADelegateFieldThrowsBeforeAnyMemoryAccess(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario($"DelegateField_{optimize}_{target}", Source,
            "DelegateField.EntryPoint", optimize, target, 0, [])
        {
            AllowUnsafe = true,
        });

        Assert.Equal(63, result);
    }

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;
        namespace DelegateField;

        public delegate int Callback(int value);

        [StructLayout(LayoutKind.Sequential)]
        public struct WithDelegate
        {
            public int A;
            [MarshalAs(UnmanagedType.LPStr)] public string S;
            public Callback H;
            public int B;
        }

        [StructLayout(LayoutKind.Sequential)]
        public sealed class Inner { public int X = 1; public Callback H; }

        [StructLayout(LayoutKind.Sequential)]
        public sealed class Outer { public int A = 7; public Inner I = new(); public int B = 8; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Element { public int Tag; public Callback H; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Elements { public int A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public Element[] Items; }

        public static unsafe class EntryPoint
        {
            private static int Twice(int value) => value * 2;

            private static bool Throws(Action action)
            {
                try { action(); return false; }
                catch (PlatformNotSupportedException) { return true; }
            }

            private static bool Untouched(IntPtr native, int size) =>
                new ReadOnlySpan<byte>((void*)native, size).IndexOfAnyExcept((byte)0xAA) < 0;

            public static int Run(int input)
            {
                var result = 0;
                var size = Marshal.SizeOf<WithDelegate>();
                var native = Marshal.AllocHGlobal(64);
                new Span<byte>((void*)native, 64).Fill(0xAA);
                // 1: the native structure stays as it was.
                if (Throws(() => Marshal.StructureToPtr(new WithDelegate { A = 1, S = "x", H = Twice, B = 2 }, native, false)) &&
                    Untouched(native, size))
                    result |= 1;
                // 2: with fDeleteOld, the old string is not freed: it still reads back after the failure.
                Marshal.StructureToPtr(new WithDelegate { A = 3, S = "old", B = 4 }, native, false);
                var old = Marshal.ReadIntPtr(native, IntPtr.Size);
                if (Throws(() => Marshal.StructureToPtr(new WithDelegate { A = 5, S = "new", H = Twice, B = 6 }, native, true)) &&
                    Marshal.ReadInt32(native) == 3 && Marshal.ReadIntPtr(native, IntPtr.Size) == old &&
                    Marshal.PtrToStringUTF8(old) == "old")
                    result |= 2;
                Marshal.DestroyStructure<WithDelegate>(native);
                // 4: a function pointer read back throws before the structure changes.
                var outer = new Outer();
                new Span<byte>((void*)native, 64).Clear();
                Marshal.WriteInt32(native, 9);
                Marshal.WriteIntPtr(native, 2 * IntPtr.Size, (IntPtr)1);
                if (Throws(() => Marshal.PtrToStructure(native, outer)) && outer.A == 7 && outer.I.X == 1 && outer.B == 8)
                    result |= 4;
                // 8: a delegate in an embedded class, written.
                new Span<byte>((void*)native, 64).Fill(0xAA);
                if (Throws(() => Marshal.StructureToPtr(new Outer { I = new Inner { H = Twice } }, native, false)) &&
                    Untouched(native, Marshal.SizeOf<Outer>()))
                    result |= 8;
                // 16: one in the second element of an array of structures.
                var elements = new Elements
                {
                    A = 1,
                    Items = new[] { new Element { Tag = 2 }, new Element { Tag = 3, H = Twice } },
                };
                if (Throws(() => Marshal.StructureToPtr(elements, native, false)) && Untouched(native, Marshal.SizeOf<Elements>()))
                    result |= 16;
                // 32: a null delegate converts to a null pointer.
                elements.Items[1].H = null;
                Marshal.StructureToPtr(elements, native, false);
                if (Marshal.ReadInt32(native) == 1 && Marshal.PtrToStructure<Elements>(native).Items[1].Tag == 3)
                    result |= 32;
                Marshal.FreeHGlobal(native);
                return result;
            }
        }
        """;
}
