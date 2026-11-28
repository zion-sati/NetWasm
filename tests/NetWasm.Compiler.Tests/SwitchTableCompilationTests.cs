// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A C# switch of 16 cases or more dispatches through a br_table in one indirect branch, where the compare chain the
// CIL switch is decoded into costs a compare per case it passes; desktop .NET's JIT jumps through a table too. A
// switch whose cases leave a protected region or reach each other (goto case), or one of fewer cases, keeps its
// chain. Each case's value is checked against desktop's.
public sealed class SwitchTableCompilationTests
{
    private const string Source = """
        public struct Pair { public int A; public int B; }

        public enum Color { C0, C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11, C12, C13, C14, C15, C16, C17, C18, C19 }

        public static class EntryPoint
        {
            public static int Plain(int x)
            {
                switch (x)
                {
                    case 0: return 3; case 1: return 7; case 2: return 11; case 3: return 13; case 4: return 17;
                    case 5: return 19; case 6: return 23; case 7: return 29; case 8: return 31; case 9: return 37;
                    case 10: return 41; case 11: return 43; case 12: return 47; case 13: return 53; case 14: return 59;
                    case 15: return 61; case 16: return 67; case 17: return 71; case 18: return 73; case 19: return 79;
                    default: return -1;
                }
            }

            public static int Shared(int x)
            {
                switch (x)
                {
                    case 0: case 1: case 2: case 3: return 5;
                    case 4: case 5: case 6: case 7: return x * 3;
                    case 8: case 9: case 10: case 11: return x + 9;
                    case 12: case 13: case 14: case 15: return x ^ 21;
                    case 16: case 17: case 18: case 19: return x - 2;
                    default: return 0;
                }
            }

            public static int InLoop(int[] values)
            {
                var sum = 0;
                foreach (var v in values)
                {
                    switch (v)
                    {
                        case 0: sum += 1; break; case 1: sum += 2; break; case 2: continue; case 3: sum *= 2; break;
                        case 4: sum -= 3; break; case 5: sum ^= 5; break; case 6: sum += v; break; case 7: continue;
                        case 8: sum += 8; break; case 9: sum -= 9; break; case 10: sum += 10; break; case 11: sum ^= 11; break;
                        case 12: sum += 12; break; case 13: sum -= 13; break; case 14: sum += 14; break; case 15: sum ^= 15; break;
                        case 16: sum += 16; break; case 17: sum -= 17; break; case 18: sum += 18; break; case 19: goto done;
                        default: sum++; break;
                    }
                    sum += 1;
                }
            done:
                return sum;
            }

            public static int States(int input)
            {
                var state = input % 20; var steps = 0; var value = input;
                while (steps < 60)
                {
                    steps++;
                    switch (state)
                    {
                        case 0: value += 1; goto case 3; case 1: value *= 3; state = 5; break; case 2: value -= 7; goto case 9;
                        case 3: value ^= 0x55; state = 11; break; case 4: value += 13; state = 0; break; case 5: value >>= 1; goto case 4;
                        case 6: value += 6; state = 19; break; case 7: value -= 7; state = 2; break; case 8: value *= 5; state = 17; break;
                        case 9: value ^= 9; state = 1; break; case 10: value += 10; state = 6; break; case 11: value -= 11; state = 7; break;
                        case 12: value += 12; state = 8; break; case 13: value -= 13; state = 10; break; case 14: value += 14; state = 12; break;
                        case 15: value -= 15; state = 13; break; case 16: value += 16; state = 14; break; case 17: value -= 17; state = 15; break;
                        case 18: value += 18; state = 16; break; case 19: return value;
                    }
                }
                return value;
            }

            public static int InTry(int x)
            {
                try
                {
                    switch (x)
                    {
                        case 0: return 2; case 1: return 4; case 2: throw new System.InvalidOperationException(); case 3: return 8;
                        case 4: return 10; case 5: return 12; case 6: return 14; case 7: return 16; case 8: return 18;
                        case 9: return 20; case 10: return 22; case 11: return 24; case 12: return 26; case 13: return 28;
                        case 14: return 30; case 15: return 32; case 16: return 34; case 17: return 36; case 18: return 38;
                        case 19: return 40; default: return 0;
                    }
                }
                catch (System.InvalidOperationException) { return -5; }
            }

            public static int Expression(int x) => x switch
            {
                0 => 100, 1 => 101, 2 => 102, 3 => 103, 4 => 104, 5 => 105, 6 => 106, 7 => 107, 8 => 108, 9 => 109,
                10 => 110, 11 => 111, 12 => 112, 13 => 113, 14 => 114, 15 => 115, 16 => 116, 17 => 117, 18 => 118, 19 => 119,
                _ => 0,
            };

            public static int Named(Color c) => c switch
            {
                Color.C0 => 1, Color.C1 => 2, Color.C2 => 3, Color.C3 => 4, Color.C4 => 5, Color.C5 => 6, Color.C6 => 7,
                Color.C7 => 8, Color.C8 => 9, Color.C9 => 10, Color.C10 => 11, Color.C11 => 12, Color.C12 => 13, Color.C13 => 14,
                Color.C14 => 15, Color.C15 => 16, Color.C16 => 17, Color.C17 => 18, Color.C18 => 19, Color.C19 => 20,
                _ => 0,
            };

            public static int Interpret(int[] code)
            {
                var pc = 0; var acc = 0;
                while (true)
                {
                    switch (code[pc++])
                    {
                        case 0: acc += 1; break; case 1: acc *= 3; break; case 2: acc -= 7; break; case 3: acc ^= 0x55; break;
                        case 4: acc += 13; break; case 5: acc >>= 1; break; case 6: acc += 6; break; case 7: return acc;
                        case 8: acc *= 5; break; case 9: acc ^= 9; break; case 10: acc += 10; break; case 11: acc -= 11; break;
                        case 12: acc += 12; break; case 13: acc -= 13; break; case 14: acc += 14; break; case 15: acc -= 15; break;
                        case 16: acc += 16; break; case 17: acc -= 17; break; case 18: pc += 1; break; case 19: acc = -acc; break;
                        default: throw new System.InvalidOperationException();
                    }
                }
            }

            public static int Machine(int input)
            {
                var state = input % 20; var value = input; var steps = 0;
                while (true)
                {
                    steps++;
                    switch (state)
                    {
                        case 0: value += 1; state = 3; break; case 1: value *= 3; state = 5; break; case 2: value -= 7; state = 9; break;
                        case 3: value ^= 0x55; state = 11; break; case 4: value += 13; state = 0; break; case 5: value >>= 1; state = 4; break;
                        case 6: value += 6; state = 19; break; case 7: value -= 7; state = 2; break; case 8: value *= 5; state = 17; break;
                        case 9: value ^= 9; state = steps > 40 ? 19 : 1; break; case 10: value += 10; state = 6; break; case 11: value -= 11; state = 7; break;
                        case 12: value += 12; state = 8; break; case 13: value -= 13; state = 10; break; case 14: value += 14; state = 12; break;
                        case 15: value -= 15; state = 13; break; case 16: value += 16; state = 14; break; case 17: value -= 17; state = 15; break;
                        case 18: value += 18; state = 16; break; case 19: return value;
                        default: return -value;
                    }
                }
            }

            public static int Filtered(int x)
            {
                try
                {
                    throw new System.InvalidOperationException();
                }
                catch (System.InvalidOperationException) when ((x switch
                {
                    0 => 1, 1 => 12, 2 => 3, 3 => 14, 4 => 5, 5 => 16, 6 => 7, 7 => 18, 8 => 9, 9 => 20,
                    10 => 11, 11 => 2, 12 => 13, 13 => 4, 14 => 15, 15 => 6, 16 => 17, 17 => 8, 18 => 19, 19 => 10,
                    _ => 0,
                }) > 10)
                {
                    return 1;
                }
                catch (System.InvalidOperationException)
                {
                    return 2;
                }
            }

            public static int Aggregate(int[] values)
            {
                var pair = new Pair();
                foreach (var v in values)
                {
                    switch (v)
                    {
                        case 0: pair.A += 1; break; case 1: pair.B += 2; break; case 2: pair.A ^= 3; break; case 3: pair.B -= 4; break;
                        case 4: pair.A += 5; break; case 5: pair.B ^= 6; break; case 6: pair.A -= 7; break; case 7: pair.B += 8; break;
                        case 8: pair.A ^= 9; break; case 9: pair.B -= 10; break; case 10: pair.A += 11; break; case 11: pair.B ^= 12; break;
                        case 12: pair.A -= 13; break; case 13: pair.B += 14; break; case 14: pair.A ^= 15; break; case 15: pair.B -= 16; break;
                        case 16: pair.A += 17; break; case 17: pair.B ^= 18; break; case 18: pair.A -= 19; break; case 19: pair.B += 20; break;
                        default: pair = new Pair { A = pair.B, B = pair.A }; break;
                    }
                }
                return pair.A * 31 + pair.B;
            }

            public static int GotoMachine(int input)
            {
                var state = input % 20; var value = input; var steps = 0;
                while (true)
                {
                    steps++;
                    switch (state)
                    {
                        case 0: value += 1; goto case 3; case 1: value *= 3; state = 5; break; case 2: value -= 7; goto case 9;
                        case 3: value ^= 0x55; state = 11; break; case 4: value += 13; state = 0; break; case 5: value >>= 1; goto case 4;
                        case 6: value += 6; state = 19; break; case 7: value -= 7; state = 2; break; case 8: value *= 5; state = 17; break;
                        case 9: value ^= 9; state = steps > 40 ? 19 : 1; break; case 10: value += 10; state = 6; break; case 11: value -= 11; state = 7; break;
                        case 12: value += 12; state = 8; break; case 13: value -= 13; state = 10; break; case 14: value += 14; state = 12; break;
                        case 15: value -= 15; state = 13; break; case 16: value += 16; state = 14; break; case 17: value -= 17; state = 15; break;
                        case 18: value += 18; state = 16; break; case 19: return value;
                        default: return -value;
                    }
                }
            }

            public static int Sixteen(int x) => x switch
            {
                0 => 9, 1 => 8, 2 => 7, 3 => 6, 4 => 5, 5 => 4, 6 => 3, 7 => 2, 8 => 1, 9 => 0, 10 => -1, 11 => -2,
                12 => -3, 13 => -4, 14 => -5, 15 => -6, _ => 60,
            };

            public static int Fifteen(int x) => x switch
            {
                0 => 9, 1 => 8, 2 => 7, 3 => 6, 4 => 5, 5 => 4, 6 => 3, 7 => 2, 8 => 1, 9 => 0, 10 => -1, 11 => -2,
                12 => -3, 13 => -4, 14 => -5, _ => 50,
            };

            public static int Run(int input)
            {
                var values = new int[64];
                for (var i = 0; i < values.Length; i++) values[i] = (i * 7 + input) % 22;
                var sum = 0;
                for (var i = -2; i < 24; i++)
                {
                    var x = i + input;
                    sum = sum * 31 + Plain(x) * 3 + Shared(x) * 5 + States(x + 40) + InTry(x) * 7 + Expression(x) +
                        Named((Color)x) + Fifteen(x) + Sixteen(x) * 13 + Filtered(x) * 11;
                }
                var code = new int[40];
                for (var i = 0; i < 39; i++)
                {
                    var op = (i * 5 + input) % 20;
                    code[i] = op == 7 || op == 18 && i >= 37 ? 0 : op;
                }
                code[39] = 7;
                return sum + InLoop(values) + Interpret(code) * 3 + Machine(input + 40) + Machine(-input - 1) + Aggregate(values) +
                    GotoMachine(input + 40) * 5 + GotoMachine(-input - 3);
            }
        }
        """;

    public static TheoryData<FinalWasmOptimization, WasmTarget> Configurations() => new()
    {
        { FinalWasmOptimization.O0, WasmTarget.Wasm32 },
        { FinalWasmOptimization.Oz, WasmTarget.Wasm32 },
        { FinalWasmOptimization.O3, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void SwitchesOfSixteenCasesDispatchThroughATable(FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimization == FinalWasmOptimization.O0
            ? assets.CompileSource($"SwitchTable_{optimization}_{target}", Source)
            : assets.CompileOptimizedSource($"SwitchTable_{optimization}_{target}", Source);
        var trace = Path.Combine(assets.Directory, "trace.txt");
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization,
            DiagnosticTracePath: trace));

        foreach (var input in new[] { 0, 3, 11 })
        {
            Assert.Equal(Expected(input),
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
        // The structure the compiler chose, from its trace; a dispatcher's table is not a switch.
        var structures = File.ReadAllText(trace);
        var printed = Print(assets, result);
        var tabled = Names(structures).Where(name => Structure(structures, name).Contains("switch IL_", StringComparison.Ordinal));
        Assert.Equal(Tabled.Order(), tabled.Order());
        foreach (var name in Tabled)
        {
            Assert.Contains("br_table", Functions(printed, name), StringComparison.Ordinal);
        }
    }

    // The methods whose switch takes a table, the loops left only from their cases (Interpret, Machine) among them, and
    // one of exactly 16 cases, whose table wasm-opt keeps at Oz and O3. The rest keep their chains: one whose cases
    // leave a try, one of 15 cases, and those with goto case, whose cases enter each other's code: States, and
    // GotoMachine, a loop left only from its switch, which the loop takes unconditionally and the chain then fills.
    private static readonly string[] Tabled =
        ["Plain", "Shared", "InLoop", "Expression", "Named", "Filtered", "Aggregate", "Interpret", "Machine", "Sixteen"];

    // The EntryPoint methods in a diagnostic trace.
    private static IEnumerable<string> Names(string trace) =>
        Regex.Matches(trace, @"^\[method EntryPoint::(\w+)\]", RegexOptions.Multiline).Select(match => match.Groups[1].Value);

    // The structure section of the method's entry in a diagnostic trace.
    private static string Structure(string trace, string name)
    {
        var header = Regex.Match(trace, $@"^\[method EntryPoint::{Regex.Escape(name)}\]", RegexOptions.Multiline);
        Assert.True(header.Success, $"{name} is not in the trace.");
        var end = trace.IndexOf("\n[method ", header.Index, StringComparison.Ordinal);
        var entry = trace[header.Index..(end < 0 ? trace.Length : end)];
        return entry[entry.IndexOf("structure:", StringComparison.Ordinal)..];
    }

    // As desktop .NET computes Run.
    private static int Expected(int input)
    {
        var values = new int[64];
        for (var i = 0; i < values.Length; i++) values[i] = (i * 7 + input) % 22;
        var sum = 0;
        for (var i = -2; i < 24; i++)
        {
            var x = i + input;
            sum = unchecked(sum * 31 + Plain(x) * 3 + Shared(x) * 5 + States(x + 40) + InTry(x) * 7 + Expression(x) +
                Named(x) + Fifteen(x) + Sixteen(x) * 13 + Filtered(x) * 11);
        }
        var code = new int[40];
        for (var i = 0; i < 39; i++)
        {
            var op = (i * 5 + input) % 20;
            code[i] = op == 7 || op == 18 && i >= 37 ? 0 : op;
        }
        code[39] = 7;
        return unchecked(sum + InLoop(values) + Interpret(code) * 3 + Machine(input + 40) + Machine(-input - 1) +
            Aggregate(values) + GotoMachine(input + 40) * 5 + GotoMachine(-input - 3));

        static int Plain(int x) => x is >= 0 and < 20 ? new[] { 3, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79 }[x] : -1;
        static int Shared(int x) => x switch
        {
            >= 0 and <= 3 => 5,
            >= 4 and <= 7 => x * 3,
            >= 8 and <= 11 => x + 9,
            >= 12 and <= 15 => x ^ 21,
            >= 16 and <= 19 => x - 2,
            _ => 0,
        };
        static int InTry(int x) => x switch { 2 => -5, >= 0 and < 20 => (x + 1) * 2, _ => 0 };
        static int Expression(int x) => x is >= 0 and < 20 ? 100 + x : 0;
        static int Named(int x) => x is >= 0 and < 20 ? x + 1 : 0;
        static int Fifteen(int x) => x is >= 0 and < 15 ? 9 - x : 50;
        static int Sixteen(int x) => x is >= 0 and < 16 ? 9 - x : 60;
        static int States(int input)
        {
            var state = input % 20; var steps = 0; var value = input;
            while (steps < 60)
            {
                steps++;
                switch (state)
                {
                    case 0: value += 1; goto case 3;
                    case 1: value *= 3; state = 5; break;
                    case 2: value -= 7; goto case 9;
                    case 3: value ^= 0x55; state = 11; break;
                    case 4: value += 13; state = 0; break;
                    case 5: value >>= 1; goto case 4;
                    case 6: value += 6; state = 19; break;
                    case 7: value -= 7; state = 2; break;
                    case 8: value *= 5; state = 17; break;
                    case 9: value ^= 9; state = 1; break;
                    case 10: value += 10; state = 6; break;
                    case 11: value -= 11; state = 7; break;
                    case 12: value += 12; state = 8; break;
                    case 13: value -= 13; state = 10; break;
                    case 14: value += 14; state = 12; break;
                    case 15: value -= 15; state = 13; break;
                    case 16: value += 16; state = 14; break;
                    case 17: value -= 17; state = 15; break;
                    case 18: value += 18; state = 16; break;
                    case 19: return value;
                }
            }
            return value;
        }
        static int Interpret(int[] code)
        {
            var pc = 0; var acc = 0;
            while (true)
            {
                switch (code[pc++])
                {
                    case 0: acc += 1; break;
                    case 1: acc *= 3; break;
                    case 2: acc -= 7; break;
                    case 3: acc ^= 0x55; break;
                    case 4: acc += 13; break;
                    case 5: acc >>= 1; break;
                    case 6: acc += 6; break;
                    case 7: return acc;
                    case 8: acc *= 5; break;
                    case 9: acc ^= 9; break;
                    case 10: acc += 10; break;
                    case 11: acc -= 11; break;
                    case 12: acc += 12; break;
                    case 13: acc -= 13; break;
                    case 14: acc += 14; break;
                    case 15: acc -= 15; break;
                    case 16: acc += 16; break;
                    case 17: acc -= 17; break;
                    case 18: pc += 1; break;
                    case 19: acc = -acc; break;
                    default: throw new System.InvalidOperationException();
                }
            }
        }

        static int Machine(int input)
        {
            var state = input % 20; var value = input; var steps = 0;
            while (true)
            {
                steps++;
                switch (state)
                {
                    case 0: value += 1; state = 3; break;
                    case 1: value *= 3; state = 5; break;
                    case 2: value -= 7; state = 9; break;
                    case 3: value ^= 0x55; state = 11; break;
                    case 4: value += 13; state = 0; break;
                    case 5: value >>= 1; state = 4; break;
                    case 6: value += 6; state = 19; break;
                    case 7: value -= 7; state = 2; break;
                    case 8: value *= 5; state = 17; break;
                    case 9: value ^= 9; state = steps > 40 ? 19 : 1; break;
                    case 10: value += 10; state = 6; break;
                    case 11: value -= 11; state = 7; break;
                    case 12: value += 12; state = 8; break;
                    case 13: value -= 13; state = 10; break;
                    case 14: value += 14; state = 12; break;
                    case 15: value -= 15; state = 13; break;
                    case 16: value += 16; state = 14; break;
                    case 17: value -= 17; state = 15; break;
                    case 18: value += 18; state = 16; break;
                    case 19: return value;
                    default: return -value;
                }
            }
        }
        static int Filtered(int x) => x is >= 0 and < 20 &&
            new[] { 1, 12, 3, 14, 5, 16, 7, 18, 9, 20, 11, 2, 13, 4, 15, 6, 17, 8, 19, 10 }[x] > 10 ? 1 : 2;
        static int Aggregate(int[] values)
        {
            int a = 0, b = 0;
            foreach (var v in values)
            {
                switch (v)
                {
                    case 0: a += 1; break;
                    case 1: b += 2; break;
                    case 2: a ^= 3; break;
                    case 3: b -= 4; break;
                    case 4: a += 5; break;
                    case 5: b ^= 6; break;
                    case 6: a -= 7; break;
                    case 7: b += 8; break;
                    case 8: a ^= 9; break;
                    case 9: b -= 10; break;
                    case 10: a += 11; break;
                    case 11: b ^= 12; break;
                    case 12: a -= 13; break;
                    case 13: b += 14; break;
                    case 14: a ^= 15; break;
                    case 15: b -= 16; break;
                    case 16: a += 17; break;
                    case 17: b ^= 18; break;
                    case 18: a -= 19; break;
                    case 19: b += 20; break;
                    default: (a, b) = (b, a); break;
                }
            }
            return a * 31 + b;
        }
        static int GotoMachine(int input)
        {
            var state = input % 20; var value = input; var steps = 0;
            while (true)
            {
                steps++;
                switch (state)
                {
                    case 0: value += 1; goto case 3;
                    case 1: value *= 3; state = 5; break;
                    case 2: value -= 7; goto case 9;
                    case 3: value ^= 0x55; state = 11; break;
                    case 4: value += 13; state = 0; break;
                    case 5: value >>= 1; goto case 4;
                    case 6: value += 6; state = 19; break;
                    case 7: value -= 7; state = 2; break;
                    case 8: value *= 5; state = 17; break;
                    case 9: value ^= 9; state = steps > 40 ? 19 : 1; break;
                    case 10: value += 10; state = 6; break;
                    case 11: value -= 11; state = 7; break;
                    case 12: value += 12; state = 8; break;
                    case 13: value -= 13; state = 10; break;
                    case 14: value += 14; state = 12; break;
                    case 15: value -= 15; state = 13; break;
                    case 16: value += 16; state = 14; break;
                    case 17: value -= 17; state = 15; break;
                    case 18: value += 18; state = 16; break;
                    case 19: return value;
                    default: return -value;
                }
            }
        }
        static int InLoop(int[] values)
        {
            var sum = 0;
            foreach (var v in values)
            {
                switch (v)
                {
                    case 0: sum += 1; break;
                    case 1: sum += 2; break;
                    case 2: continue;
                    case 3: sum *= 2; break;
                    case 4: sum -= 3; break;
                    case 5: sum ^= 5; break;
                    case 6: sum += v; break;
                    case 7: continue;
                    case 8: sum += 8; break;
                    case 9: sum -= 9; break;
                    case 10: sum += 10; break;
                    case 11: sum ^= 11; break;
                    case 12: sum += 12; break;
                    case 13: sum -= 13; break;
                    case 14: sum += 14; break;
                    case 15: sum ^= 15; break;
                    case 16: sum += 16; break;
                    case 17: sum -= 17; break;
                    case 18: sum += 18; break;
                    case 19: goto done;
                    default: sum++; break;
                }
                sum += 1;
            }
        done:
            return sum;
        }
    }

    private static string Print(TestAssets assets, CompilationResult result)
    {
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return printed.StandardOutput;
    }

    // The method's function in the printed module; Filtered's filter is a function of its own, the module's only one.
    private static string Functions(string printed, string name)
    {
        var function = name == "Filtered" ? @"\$filter\.\d+" : $@"\$EntryPoint::{Regex.Escape(name)}\b";
        var functions = Regex.Matches(printed, $@"\(func {function}.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline);
        Assert.Single(functions);
        return functions[0].Value;
    }
}
