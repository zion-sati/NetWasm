// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Raw P/Invokes that take desktop .NET's runtime marshalling of handles, HandleRefs and arrays of blittable elements
// compile to marshalling stubs that call one import with the native signature; what that marshalling would convert
// fails the build where a program reaches it. The harness links no native library, so the tests read the module's
// imports; the native integration tests run the stubs against desktop .NET.
public sealed class NativeMarshallingStubCompilationTests
{
    private const string Declarations = """
        using System;
        using System.Runtime.InteropServices;
        namespace NativeMarshallingFixture;

        public sealed class Handle : SafeHandle
        {
            private Handle() : base(IntPtr.Zero, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => true;
        }

        public sealed class Critical : CriticalHandle
        {
            private Critical() : base(IntPtr.Zero) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => true;
        }

        public abstract class AbstractHandle : SafeHandle
        {
            protected AbstractHandle() : base(IntPtr.Zero, true) { }
        }

        public sealed class NoConstructorHandle : SafeHandle
        {
            public NoConstructorHandle(IntPtr value) : base(value, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => true;
        }

        public struct Point { public int X; public short Y; }
        public struct Flagged { public bool Set; }
        // A volatile field's type carries a required modifier; the struct is still blittable.
        public struct Shared { public volatile int Value; }
        public enum Color { Red, Green }
        [StructLayout(LayoutKind.Auto)] public struct Loose { public int X; }
        public sealed class Plain { }

        public static class Native
        {
            [DllImport("mule", EntryPoint = "mule_borrow")] public static extern int Borrow(Handle handle);
            [DllImport("mule", EntryPoint = "mule_borrow")] public static extern int BorrowCritical(Critical handle);
            [DllImport("mule", EntryPoint = "mule_borrow")] public static extern int BorrowReference(HandleRef handle);
            [DllImport("mule", EntryPoint = "mule_create")] public static extern Handle Create();
            [DllImport("mule", EntryPoint = "mule_open")] public static extern int Open(out Handle handle);
            [DllImport("mule", EntryPoint = "mule_open")] public static extern int Replace(ref Critical handle);
            [DllImport("mule", EntryPoint = "mule_open")] public static extern int ReplaceSafe(ref Handle handle);
            [DllImport("mule", EntryPoint = "mule_open")] public static extern int ReplaceIn([In] ref Handle handle);
            [DllImport("mule", EntryPoint = "mule_release")] public static extern void Release(Handle handle);
            [DllImport("mule", EntryPoint = "mule_peek")] public static extern int Peek(in Point point, Handle handle);
            [DllImport("mule", EntryPoint = "mule_points")] public static extern int Colors(Color[] colors, int count);
            [DllImport("mule", EntryPoint = "mule_points")] public static extern int Shareds(Shared[] values, int count);
            [DllImport("mule", EntryPoint = "mule_points")] public static extern unsafe int Pointers(byte*[] values, int count);
            [DllImport("mule", EntryPoint = "mule_points")] public static extern int Objects(object[] values);
            [DllImport("mule", EntryPoint = "mule_points")] public static extern int Plains(Plain[] values);
            [DllImport("mule", EntryPoint = "mule_points")] public static extern int Looses(Loose[] values, int count);
            [DllImport("mule", EntryPoint = "mule_open")] public static extern int Update(ref Plain value);
            [DllImport("mule", EntryPoint = "mule_points")] public static extern int Points(Point[] points, int count);
            [DllImport("mule", EntryPoint = "mule_points")] public static extern int Bytes(byte[] bytes, int count);

            [DllImport("mule", EntryPoint = "mule_text")] public static extern int Text(string text);
            [DllImport("mule", EntryPoint = "mule_callback")] public static extern int Callback(Func<int> callback);
            [DllImport("mule", EntryPoint = "mule_flags")] public static extern int Flags(bool[] flags);
            [DllImport("mule", EntryPoint = "mule_flags")] public static extern int Structs(Flagged[] flags);
            [DllImport("mule", EntryPoint = "mule_matrix")] public static extern int Matrix(int[,] matrix);
            [DllImport("mule", EntryPoint = "mule_resize")] public static extern int Resize(ref byte[] bytes);
            [DllImport("mule", EntryPoint = "mule_create")] public static extern AbstractHandle CreateAbstract();
            [DllImport("mule", EntryPoint = "mule_open")] public static extern int OpenWithoutConstructor(out NoConstructorHandle handle);
            [DllImport("mule", EntryPoint = "mule_name")] public static extern string Name();
            [DllImport("mule", EntryPoint = "mule_create")] public static extern HandleRef CreateReference();
            [DllImport("mule", EntryPoint = "mule_object")] public static extern int Object(object value);
            [DllImport("mule", EntryPoint = "mule_count")] public static extern int Count();

            [DllImport("mule", EntryPoint = "mule_bool")] public static extern bool Bool(bool value);
            [DllImport("mule", EntryPoint = "mule_byte_bool")] [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool ByteBool([MarshalAs(UnmanagedType.I1)] bool value);
            [DllImport("mule", EntryPoint = "mule_ansi")] public static extern char Ansi(char value);
            [DllImport("mule", EntryPoint = "mule_wide", CharSet = CharSet.Unicode)] public static extern char Wide(char value);
            [DllImport("mule", EntryPoint = "mule_bump")] public static extern void Bump(ref bool flag, out char letter, [In] ref char wide);
            [DllImport("mule", EntryPoint = "mule_same")] public static extern int Same([MarshalAs(UnmanagedType.U4)] int value);
            [DllImport("mule", EntryPoint = "mule_variant")] public static extern int Variant([MarshalAs(UnmanagedType.VariantBool)] bool value);
            [DllImport("mule", EntryPoint = "mule_wide_int")] public static extern int WideInt([MarshalAs(UnmanagedType.I4)] char value);
            [DllImport("mule", EntryPoint = "mule_short")] public static extern int Short([MarshalAs(UnmanagedType.I2)] int value);
            [DllImport("mule", EntryPoint = "mule_variant")] [return: MarshalAs(UnmanagedType.VariantBool)] public static extern bool VariantResult();

            [DllImport("mule", EntryPoint = "mule_wide_text", CharSet = CharSet.Unicode)] public static extern int WideText(string text);
            [DllImport("mule", EntryPoint = "mule_utf8")] public static extern int Utf8([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
            [DllImport("mule", EntryPoint = "mule_wide_name")] [return: MarshalAs(UnmanagedType.LPWStr)] public static extern string WideName();
            [DllImport("mule", EntryPoint = "mule_numbered_name")] [return: MarshalAs(UnmanagedType.I4)] public static extern string NumberedName();
            [DllImport("mule", EntryPoint = "mule_rename")] public static extern int Rename(ref string text);
            [DllImport("mule", EntryPoint = "mule_numbered")] public static extern int Numbered([MarshalAs(UnmanagedType.I4)] string text);
            [DllImport("mule", EntryPoint = "mule_basic")] public static extern int Basic([MarshalAs(UnmanagedType.BStr)] string text);
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void HandlesHandleRefsAndBlittableArraysCallOneNativeImportEach(WasmTarget target)
    {
        var imports = Compile("""
            Native.Create(); Native.Open(out var opened); Native.Borrow(opened); Native.BorrowCritical(null!);
            Native.BorrowReference(new HandleRef(null, 1)); Critical critical = null!; Native.Replace(ref critical);
            Native.ReplaceSafe(ref opened); Native.ReplaceIn(ref opened); Native.Release(opened); Native.Peek(new Point(), opened);
            Native.Colors([Color.Red], 1); Native.Shareds([new Shared()], 1); unsafe { Native.Pointers(new byte*[1], 1); }
            Native.Points([new Point()], 1); Native.Bytes([1], 1);
            """, target);

        Assert.Equal(["mule_borrow", "mule_create", "mule_open", "mule_peek", "mule_points", "mule_release"],
            imports.Select(import => import.Name).Where(name => name.StartsWith("mule_", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }

    // bool and char pass as desktop .NET's runtime marshalling passes them on Unix (BOOL, one-byte bool, ANSI byte,
    // UTF-16 code unit), each P/Invoke through a stub over one import; a restating [MarshalAs] changes nothing.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BoolsAndCharsCallOneNativeImportEach(WasmTarget target)
    {
        var imports = Compile("""
            var flag = Native.Bool(true) && Native.ByteBool(false); var wide = Native.Wide(Native.Ansi('A'));
            Native.Bump(ref flag, out var letter, ref wide); Native.Same(letter);
            """, target);

        Assert.Equal(["mule_ansi", "mule_bool", "mule_bump", "mule_byte_bool", "mule_same", "mule_wide"],
            imports.Select(import => import.Name).Where(name => name.StartsWith("mule_", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }

    // String arguments pass as desktop .NET's runtime marshalling passes them on Unix (a UTF-8 copy, or the string's
    // UTF-16 characters, NUL-terminated), each P/Invoke through a stub over one import.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StringsCallOneNativeImportEach(WasmTarget target)
    {
        var imports = Compile("""
            Native.Text(null!); Native.WideText("x"); Native.Utf8("x");
            """, target);

        Assert.Equal(["mule_text", "mule_utf8", "mule_wide_text"],
            imports.Select(import => import.Name).Where(name => name.StartsWith("mule_", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }

    // A UTF-8 string argument's native copy is freed once the call returns, as desktop .NET's IL stub frees it; a
    // UTF-16 one is the string itself, pinned and kept alive; a string result is decoded and its native buffer freed
    // with the C library's free (inlining off, so the stub keeps its calls).
    [Fact]
    public void AStubFreesTheNativeStringsItOwns()
    {
        var result = CompileResult("""
            Native.Text("x"); Native.WideText("x"); _ = Native.Name(); _ = Native.WideName();
            """, WasmTarget.Wasm32, inlining: false);
        string[] Calls(string stub) => [.. result.Program.Methods.Values
            .Single(body => body.Method.DeclaringType.FullName == "NativeMarshallingFixture.Native" &&
                body.Method.Definition.Name == stub)
            .Body.Instructions.Where(instruction => instruction.Operation == CilOperation.Call)
            .Select(instruction => ((CilOperand.MethodInstance)instruction.Operand).Value)
            .Select(method => $"{method.DeclaringType.FullName}.{method.Definition.Name}")];

        Assert.Equal(["System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8",
            "System.Runtime.InteropServices.Marshal.FreeCoTaskMem"],
            Calls("Text").Where(call => call.StartsWith("System.", StringComparison.Ordinal)));
        // GetPinnableReference is the string's data address, which the compiler lowers in place.
        Assert.Equal(["System.GC.KeepAlive"],
            Calls("WideText").Where(call => call.StartsWith("System.", StringComparison.Ordinal)));
        Assert.Equal(["System.Runtime.InteropServices.Marshal.PtrToStringUTF8",
            "System.Runtime.InteropServices.Marshal.FreeCoTaskMem"],
            Calls("Name").Where(call => call.StartsWith("System.", StringComparison.Ordinal)));
        Assert.Equal(["System.Runtime.InteropServices.Marshal.PtrToStringUni",
            "System.Runtime.InteropServices.Marshal.FreeCoTaskMem"],
            Calls("WideName").Where(call => call.StartsWith("System.", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("var text = \"\"; Native.Rename(ref text);", "must be a SafeHandle or CriticalHandle")]
    [InlineData("Native.Numbered(\"x\");", "a string must be paired with LPStr, LPWStr or LPUTF8Str")]
    [InlineData("Native.NumberedName();", "a string must be paired with LPStr, LPWStr or LPUTF8Str")]
    [InlineData("Native.Basic(\"x\");", "runtime marshalling are unsupported")]
    [InlineData("Native.Variant(true);", "booleans must be paired with I1, U1, or Bool")]
    [InlineData("Native.VariantResult();", "booleans must be paired with I1, U1, or Bool")]
    [InlineData("Native.WideInt('a');", "chars must be paired with U1, I1, U2 or I2")]
    [InlineData("Native.Short(1);", "Invalid managed/unmanaged type combination")]
    [InlineData("Native.Callback(() => 1);", "delegates and other classes")]
    [InlineData("Native.Object(new object());", "delegates and other classes")]
    [InlineData("Native.Flags([true]);", "blittable elements")]
    [InlineData("Native.Objects([new object()]);", "blittable elements")]
    [InlineData("Native.Plains([new Plain()]);", "blittable elements")]
    [InlineData("Native.Looses([new Loose()], 1);", "blittable elements")]
    [InlineData("var plain = new Plain(); Native.Update(ref plain);", "must be a SafeHandle or CriticalHandle")]
    [InlineData("Native.Structs([new Flagged()]);", "blittable elements")]
    [InlineData("Native.Matrix(new int[1, 1]);", "single-dimensional")]
    [InlineData("byte[] bytes = []; Native.Resize(ref bytes);", "must be a SafeHandle or CriticalHandle")]
    [InlineData("Native.CreateAbstract();", "must not be abstract")]
    [InlineData("Native.OpenWithoutConstructor(out _);", "parameterless constructor")]
    [InlineData("Native.CreateReference();", "HandleRef cannot be a native result")]
    public void WhatRuntimeMarshallingWouldConvertFailsWhereReached(string call, string reason)
    {
        var error = Assert.Throws<CompilerException>(() => Compile(call, WasmTarget.Wasm32));

        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Contains(reason, error.Diagnostic.Message, StringComparison.Ordinal);
    }

    // Desktop .NET rejects handles under disabled runtime marshalling too.
    [Fact]
    public void DisabledRuntimeMarshallingRejectsHandles()
    {
        var error = Assert.Throws<CompilerException>(() => Compile("Native.Borrow(null!);", WasmTarget.Wasm32,
            "[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]\n"));

        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
    }

    // A call through an unmanaged function pointer marshals as a P/Invoke without attributes does (BOOL, ANSI char,
    // UTF-8 string, a struct that holds a bool as its native copy): the calli calls a stub, which takes the arguments
    // and then the pointer and calls the pointer with the native signature. Under disabled runtime marshalling the
    // calli stays, bool and char as C's.
    [Theory]
    [InlineData(WasmTarget.Wasm32, "")]
    [InlineData(WasmTarget.Wasm64, "")]
    [InlineData(WasmTarget.Wasm32, "[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]\n")]
    public void FunctionPointerCallsTakeTheAssemblysMarshalling(WasmTarget target, string prefix)
    {
        var disabled = prefix.Length != 0;
        var result = CompileResult(disabled
            ? "unsafe { var call = (delegate* unmanaged<bool, char, int>)(void*)(nint)input; call(true, 'A'); }"
            : """
            unsafe
            {
                var call = (delegate* unmanaged<bool, char, string, int>)(void*)(nint)input; call(true, 'A', "x");
                var flagged = (delegate* unmanaged<Flagged, bool>)(void*)(nint)input; flagged(new() { Set = true });
            }
            """, target, prefix, inlining: false);
        var stubs = result.Program.Methods.Values
            .Where(body => body.Method.Definition.Name.StartsWith("<calli>", StringComparison.Ordinal)).ToArray();
        var sites = result.Program.Methods.Values.SelectMany(body => body.Body.Instructions)
            .Select(instruction => instruction.Operand).OfType<CilOperand.CallSite>()
            .Select(site => (site.Unmanaged, Types: string.Join(",", site.Signature.ParameterSignatureTypes
                .Prepend(site.Signature.ReturnSignatureType).Select(type => type.CanonicalName))))
            .Order().ToArray();

        if (disabled)
        {
            Assert.Empty(stubs);
            Assert.Equal([(true, "primitive:i4,primitive:bool,primitive:char")], sites);
            return;
        }
        Assert.Equal(2, stubs.Length);
        var nativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt).CanonicalName;
        // The one-field struct passes as its single scalar, its bool's BOOL.
        Assert.Equal([(true, "primitive:i4,primitive:i4"), (true, $"primitive:i4,primitive:i4,primitive:u1,{nativeInt}")],
            sites);
    }

    // A struct of another assembly, or a struct with another assembly's struct or enum fields, lays out natively as one
    // of the calling assembly does, as on desktop .NET, even when that assembly disables runtime marshalling: P/Invokes
    // and unmanaged function-pointer calls that name them compile to the same stubs and native signatures whichever
    // assembly declares the structs.
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    public void StructsOfOtherAssembliesLayOutNatively(WasmTarget target, bool libraryDisablesMarshalling)
    {
        var local = CrossAssemblyShape(target, separate: false, libraryDisablesMarshalling: false);
        var separate = CrossAssemblyShape(target, separate: true, libraryDisablesMarshalling);

        Assert.Equal(local.Stubs, separate.Stubs);
        Assert.Equal(local.Sites, separate.Sites);
        Assert.Equal(local.Imports, separate.Imports);
        Assert.Equal(5, separate.Stubs);
        var nativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt).CanonicalName;
        // The one-char struct result is its ANSI byte; the two-field struct passes as its native copy's address.
        Assert.Equal([(true, $"primitive:u1,{nativeInt}")], separate.Sites);
        var pointer = target == WasmTarget.Wasm64 ? WasmValueType.I64 : WasmValueType.I32;
        Assert.Contains(("cross_flagged", $"{pointer}", $"{WasmValueType.I32}"), separate.Imports);
    }

    private static (int Stubs, (bool Unmanaged, string Types)[] Sites, (string EntryPoint, string Parameters, string Result)[] Imports)
        CrossAssemblyShape(WasmTarget target, bool separate, bool libraryDisablesMarshalling)
    {
        const string library = """
            namespace NativeStructLibrary;

            public struct Flagged { public bool Set; public int Count; }
            public struct Letter { public char Value; }
            public enum Mode : byte { Off, On }
            """;
        const string fixture = """
            using System.Runtime.InteropServices;
            using NativeStructLibrary;

            namespace CrossAssemblyFixture;

            public struct Holder { public Flagged Inner; public Mode Mode; }
            public struct WithMode { public bool On; public Mode Mode; }

            public static unsafe class EntryPoint
            {
                [DllImport("mule", EntryPoint = "cross_flagged")] private static extern int TakeFlagged(Flagged value);
                [DllImport("mule", EntryPoint = "cross_letter")] private static extern Letter MakeLetter(ref Flagged value);
                [DllImport("mule", EntryPoint = "cross_holder")] private static extern int TakeHolder(Holder holder);
                [DllImport("mule", EntryPoint = "cross_mode")] private static extern int TakeMode(WithMode value);

                public static int Run(int input)
                {
                    if (input != 12345) return input + 1;
                    var flagged = new Flagged { Set = true, Count = 2 };
                    var letter = MakeLetter(ref flagged);
                    var call = (delegate* unmanaged<Flagged, Letter>)(void*)(nint)input;
                    return TakeFlagged(flagged) + letter.Value + call(flagged).Value +
                        TakeHolder(new() { Inner = flagged, Mode = Mode.On }) + TakeMode(new() { On = true, Mode = Mode.Off });
                }
            }
            """;
        using var assets = TestAssets.Create();
        string assembly;
        ImmutableArray<string> references;
        if (separate)
        {
            var libraryPath = assets.CompileUnsafeSource("NativeStructLibrary", libraryDisablesMarshalling
                ? "[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]\n" + library
                : library);
            assembly = assets.CompileUnsafeSource("CrossAssemblyFixture", fixture, libraryPath);
            references = [assets.CoreLib, libraryPath];
        }
        else
        {
            assembly = assets.CompileUnsafeSource("CrossAssemblyFixture",
                fixture.Replace("using NativeStructLibrary;\n", "", StringComparison.Ordinal) +
                library.Replace("namespace NativeStructLibrary;", "", StringComparison.Ordinal));
            references = [assets.CoreLib];
        }
        var result = NetWasmCompiler.Compile(new(assembly, references, "CrossAssemblyFixture.EntryPoint", "Run",
            [new("run", "CrossAssemblyFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            OptimizationOverrides: new(ManagedMethodInlining: ManagedMethodInlining.Disabled)));
        var stubs = result.Program.Methods.Values.Count(body => body.Method.Definition.NativeMarshallingStub is not null);
        var sites = result.Program.Methods.Values.SelectMany(body => body.Body.Instructions)
            .Select(instruction => instruction.Operand).OfType<CilOperand.CallSite>()
            .Select(site => (site.Unmanaged, Types: string.Join(",", site.Signature.ParameterSignatureTypes
                .Prepend(site.Signature.ReturnSignatureType).Select(type => type.CanonicalName))))
            .Order().ToArray();
        var imports = result.NativeImports
            .Select(import => (import.EntryPoint, string.Join(",", import.Parameters), $"{import.ReturnType}"))
            .Order().ToArray();
        return (stubs, sites, imports);
    }

    // An exception filter's frame is its method's, which includes the method's direct native calls.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AMethodWithAFilterCallsNativeCode(WasmTarget target)
    {
        var imports = Compile("""
            try { Native.Count(); }
            catch (InvalidOperationException error) when (error.Message.Length > 0) { return 1; }
            """, target);

        Assert.Contains(imports, import => import.Name == "mule_count");
    }

    private static IReadOnlyList<(string Module, string Name)> Compile(string body, WasmTarget target, string prefix = "") =>
        WasmModuleInspection.ReadFunctionImports(CompileResult(body, target, prefix: prefix).ApplicationModule);

    private static CompilationResult CompileResult(string body, WasmTarget target, string prefix = "", bool inlining = true)
    {
        using var assets = TestAssets.Create();
        var source = Declarations.Replace("namespace NativeMarshallingFixture;", prefix + "namespace NativeMarshallingFixture;",
            StringComparison.Ordinal);
        var assembly = assets.CompileUnsafeSource("NativeMarshallingFixture", source + $$"""

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (input != 12345) return input + 1;
                    {{body}}
                    return 0;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "NativeMarshallingFixture.EntryPoint", "Run",
            [new("run", "NativeMarshallingFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            OptimizationOverrides: inlining ? null : new(ManagedMethodInlining: ManagedMethodInlining.Disabled)));
        return result;
    }
}
