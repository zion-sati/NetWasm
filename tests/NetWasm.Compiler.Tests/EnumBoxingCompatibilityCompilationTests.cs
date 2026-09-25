using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumBoxingCompatibilityCompilationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerUnboxesEnumsThroughTheirUnderlyingStorageType(bool optimized)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            namespace EnumBoxingCompatibilityFixture;

            public enum I1A : sbyte { Value = 1 }
            public enum I1B : sbyte { Value = 1 }
            public enum U1A : byte { Value = 2 }
            public enum U1B : byte { Value = 2 }
            public enum I2A : short { Value = 3 }
            public enum I2B : short { Value = 3 }
            public enum U2A : ushort { Value = 4 }
            public enum U2B : ushort { Value = 4 }
            public enum I4A : int { Value = 5 }
            public enum I4B : int { Value = 5 }
            public enum U4A : uint { Value = 6 }
            public enum U4B : uint { Value = 6 }
            public enum I8A : long { Value = 7 }
            public enum I8B : long { Value = 7 }
            public enum U8A : ulong { Value = 8 }
            public enum U8B : ulong { Value = 8 }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (Convert<sbyte>(I1A.Value) != 1 ||
                        Convert<I1A>((sbyte)1) != I1A.Value ||
                        Convert<I1B>(I1A.Value) != I1B.Value ||
                        !Reject<U1A>(I1A.Value)) return 1;
                    if (Convert<byte>(U1A.Value) != 2 ||
                        Convert<U1A>((byte)2) != U1A.Value ||
                        Convert<U1B>(U1A.Value) != U1B.Value ||
                        !Reject<I2A>(U1A.Value)) return 2;
                    if (Convert<short>(I2A.Value) != 3 ||
                        Convert<I2A>((short)3) != I2A.Value ||
                        Convert<I2B>(I2A.Value) != I2B.Value ||
                        !Reject<U2A>(I2A.Value)) return 3;
                    if (Convert<ushort>(U2A.Value) != 4 ||
                        Convert<U2A>((ushort)4) != U2A.Value ||
                        Convert<U2B>(U2A.Value) != U2B.Value ||
                        !Reject<I4A>(U2A.Value)) return 4;
                    if (Convert<int>(I4A.Value) != 5 ||
                        Convert<I4A>(5) != I4A.Value ||
                        Convert<I4B>(I4A.Value) != I4B.Value ||
                        !Reject<U4A>(I4A.Value)) return 5;
                    if (Convert<uint>(U4A.Value) != 6 ||
                        Convert<U4A>(6U) != U4A.Value ||
                        Convert<U4B>(U4A.Value) != U4B.Value ||
                        !Reject<I8A>(U4A.Value)) return 6;
                    if (Convert<long>(I8A.Value) != 7 ||
                        Convert<I8A>(7L) != I8A.Value ||
                        Convert<I8B>(I8A.Value) != I8B.Value ||
                        !Reject<U8A>(I8A.Value)) return 7;
                    if (Convert<ulong>(U8A.Value) != 8 ||
                        Convert<U8A>(8UL) != U8A.Value ||
                        Convert<U8B>(U8A.Value) != U8B.Value ||
                        !Reject<I1A>(U8A.Value)) return 8;
                    return 0;
                }

                private static T Convert<T>(object value) where T : struct =>
                    (T)value;

                private static bool Reject<T>(object value) where T : struct
                {
                    try
                    {
                        _ = (T)value;
                        return false;
                    }
                    catch (InvalidCastException)
                    {
                        return true;
                    }
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = executor.Execute(new CompilationScenario(
                optimized ? "EnumBoxingCompatibilityOptimized" : "EnumBoxingCompatibilityDebug",
                source,
                "EnumBoxingCompatibilityFixture.EntryPoint",
                optimized,
                target,
                0,
                []));

            Assert.Equal(0, result);
        }
    }
}
