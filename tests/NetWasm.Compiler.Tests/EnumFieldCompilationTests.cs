// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumFieldCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesExternalEnumFieldsAndPropertiesForEveryWidth(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("EnumField.Library", LibrarySource);
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "EnumField.Application",
            ApplicationSource,
            "EnumField.Application.EntryPoint",
            optimize,
            target,
            0,
            ImmutableArray.Create(library)));

        Assert.Equal(0, result);
    }

    private const string LibrarySource = """
        namespace EnumField.Library;

        public enum I1 : sbyte { Value = -8 }
        public enum U1 : byte { Value = 248 }
        public enum I2 : short { Value = -30000 }
        public enum U2 : ushort { Value = 60000 }
        public enum I4 : int { Value = -2000000000 }
        public enum U4 : uint { Value = 4000000000 }
        public enum I8 : long { Value = -8000000000000000000 }
        public enum U8 : ulong { Value = 16000000000000000000 }
        """;

    private const string ApplicationSource = """
        using EnumField.Library;

        namespace EnumField.Application;

        public sealed class Holder
        {
            public I1 I1Field;
            public U1 U1Field;
            public I2 I2Field;
            public U2 U2Field;
            public static I4 I4Field;
            public static U4 U4Field;
            public I8 I8Property { get; set; }
            public static U8 U8Property { get; set; }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var holder = new Holder
                {
                    I1Field = I1.Value,
                    U1Field = U1.Value,
                    I2Field = I2.Value,
                    U2Field = U2.Value,
                    I8Property = I8.Value,
                };
                Holder.I4Field = I4.Value;
                Holder.U4Field = U4.Value;
                Holder.U8Property = U8.Value;

                var failures = holder.I1Field == I1.Value ? 0 : 1;
                failures |= holder.U1Field == U1.Value ? 0 : 2;
                failures |= holder.I2Field == I2.Value ? 0 : 4;
                failures |= holder.U2Field == U2.Value ? 0 : 8;
                failures |= Holder.I4Field == I4.Value ? 0 : 16;
                failures |= Holder.U4Field == U4.Value ? 0 : 32;
                failures |= holder.I8Property == I8.Value ? 0 : 64;
                failures |= Holder.U8Property == U8.Value ? 0 : 128;
                return failures;
            }
        }
        """;
}
