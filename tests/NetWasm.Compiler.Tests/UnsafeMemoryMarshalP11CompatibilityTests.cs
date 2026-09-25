using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class UnsafeMemoryMarshalP11CompatibilityTests
{
    [Fact]
    public void CompilerLowersP11UnsafeAndMemoryMarshalReferenceContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "UnsafeMemoryMarshalP11Fixture",
            """
            namespace UnsafeMemoryMarshalP11Fixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = input;
                    var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                        ref value,
                        1);
                    span[0]++;

                    var readOnly = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                        in value,
                        1);
                    ref byte firstByte = ref System.Runtime.CompilerServices.Unsafe.As<int, byte>(
                        ref value);
                    var copied = System.Runtime.CompilerServices.Unsafe.ReadUnaligned<int>(in firstByte);

                    System.Array array = new[] { copied, 2 };
                    ref byte arrayData = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(array);
                    ref int first = ref System.Runtime.CompilerServices.Unsafe.As<byte, int>(ref arrayData);
                    first++;

                    return readOnly[0] + first;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UnsafeMemoryMarshalP11Fixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(85, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
