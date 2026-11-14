using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BitVector32ContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MasksSectionsAndFormattingMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "BitVectorFixture", Source, "BitVectorFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections.Specialized;
        namespace BitVectorFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var bits = new BitVector32(0); var mask = BitVector32.CreateMask();
                for (var i = 0; i < 32; i++)
                {
                    if (bits[mask]) return 1;
                    bits[mask] = true; if (!bits[mask]) return 2;
                    if (i != 31) mask = BitVector32.CreateMask(mask);
                }
                if (bits.Data != -1 || !bits[0] || !bits[3]) return 3;
                try { BitVector32.CreateMask(mask); return 4; } catch (InvalidOperationException) { }
                bits[3] = false; if (bits.Data != -4 || bits[3]) return 5;
                var copy = new BitVector32(bits);
                if (!copy.Equals(bits) || !copy.Equals((object)bits) || copy.Equals(null) || copy.GetHashCode() != bits.GetHashCode()) return 6;
                if (BitVector32.ToString(new BitVector32(1)) != "BitVector32{00000000000000000000000000000001}") return 7;
                if (new BitVector32(int.MinValue).ToString() != "BitVector32{10000000000000000000000000000000}") return 8;
                var a = BitVector32.CreateSection(3);
                var b = BitVector32.CreateSection(5, a);
                if (a.Mask != 3 || a.Offset != 0 || b.Mask != 7 || b.Offset != 2) return 9;
                bits = new BitVector32(0); bits[a] = 2; bits[b] = 5;
                if (bits[a] != 2 || bits[b] != 5 || bits.Data != 22) return 10;
                bits[b] = 15; if (bits[b] != 7 || bits[a] != 2) return 11;
                bits[b] = -1; if (bits[b] != 7) return 12;
                var equal = BitVector32.CreateSection(3);
                if (a != equal || !(a == equal) || !a.Equals((object)equal) || a.Equals(null) || a == b || a.GetHashCode() != equal.GetHashCode()) return 13;
                if (b.ToString() != "Section{0x7, 0x2}" || BitVector32.Section.ToString(a) != "Section{0x3, 0x0}") return 14;
                try { BitVector32.CreateSection(0); return 15; } catch (ArgumentOutOfRangeException) { }
                try { BitVector32.CreateSection(-1); return 16; } catch (ArgumentOutOfRangeException) { }
                var large = BitVector32.CreateSection(short.MaxValue);
                var second = BitVector32.CreateSection(short.MaxValue, large);
                var last = BitVector32.CreateSection(7, second);
                if (last.Offset != 30 || last.Mask != 7) return 17;
                bits = new BitVector32(0); bits[last] = 7;
                if (bits[last] != 3 || bits.Data != unchecked((int)0xc0000000)) return 18;
                try { BitVector32.CreateSection(1, last); return 19; } catch (InvalidOperationException) { }
                return input + 1;
            }
        }
        """;
}
