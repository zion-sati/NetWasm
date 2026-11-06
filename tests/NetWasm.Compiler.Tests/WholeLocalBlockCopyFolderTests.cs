using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Which block copies become a copy of the local: sizeof the locals' type, a
// constant of a field-less struct's declared size, and the ref form through
// Unsafe.As, unaligned; not a part of the struct, a constant for a struct with
// fields, or locals of two types. A folded copy whose destination the block
// then reads leaves no destination at all.
public sealed class WholeLocalBlockCopyFolderTests
{
    [Theory]
    [InlineData("Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));", true)]
    [InlineData("Unsafe.CopyBlockUnaligned(ref Unsafe.As<Big, byte>(ref destination), ref Unsafe.As<Big, byte>(ref source), (uint)Unsafe.SizeOf<Big>());", true)]
    [InlineData("Unsafe.CopyBlock(&rawDestination, &rawSource, 64);", true)]
    [InlineData("Unsafe.CopyBlock(&destination, &source, 16);", false)]
    [InlineData("Unsafe.CopyBlock(&destination, &source, 32);", false)]
    [InlineData("Unsafe.CopyBlock(&other, &source, (uint)sizeof(Big));", false)]
    public void FoldsOnlyACopyOfAWholeLocalIntoAnotherOfItsType(string copy, bool folded)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("WholeLocalBlockCopies", $$"""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public struct Big { public long A, B, C, D; }

            public struct Other { public long A, B, C, D; }

            [StructLayout(LayoutKind.Explicit, Size = 64)]
            public struct Raw { }

            public static unsafe class EntryPoint
            {
                public static int Run(int input) => (int)Probe(input);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Probe(int input)
                {
                    var source = new Big { A = input, B = 2, C = 3, D = 4 };
                    var destination = default(Big);
                    var other = default(Other);
                    Raw rawSource = default, rawDestination = default;
                    {{copy}}
                    return destination.A + destination.D + other.A + *(byte*)&rawDestination + *(byte*)&rawSource;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3));
        var body = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Probe").Body;

        Assert.Equal(!folded, body.Instructions.Any(static instruction => instruction.Operation == CilOperation.CopyBlock));
    }

    // `destination = source` alone: nothing stores the destination and every
    // read of it reads the source.
    [Fact]
    public void AFoldedCopyReadInItsBlockLeavesNoDestination()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("WholeLocalBlockCopyReads", """
            using System.Runtime.CompilerServices;

            public struct Big { public long A, B, C, D; }

            public static unsafe class EntryPoint
            {
                private static Big s_field;

                public static int Run(int input) { Probe(); return (int)s_field.A; }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Probe()
                {
                    var source = s_field;
                    Big destination;
                    Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                    s_field = destination;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3));
        var body = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Probe").Body;
        var bigLocals = Enumerable.Range(0, body.LocalSignatureTypes.Length)
            .Where(local => body.LocalSignatureTypes[local].FullName == "Big").ToHashSet();

        Assert.DoesNotContain(body.Instructions, static instruction =>
            instruction.Operation is CilOperation.CopyBlock or CilOperation.LoadLocalAddress);
        Assert.Single(body.Instructions, instruction => instruction is
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } && bigLocals.Contains(local));
    }
}
