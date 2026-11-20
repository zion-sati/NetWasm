// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A delegate over a virtual method of thousands of receiver types (ldvirtftn), a function pointer that can hold
// any of a thousand-odd static methods (calli), and hundreds of delegate types combined (each combination clones a
// delegate) each choose among their candidates by a search. A test of each candidate in turn nested an if per
// candidate, deeper than wasm-opt's worker threads can recurse on macOS's 512 KiB thread stacks.
public sealed class ManyCandidateDispatchCompilationTests
{
    private const int Receivers = 3_000;
    private const int Pointers = 1_500;
    private const int Delegates = 600;

    // The search's halvings, the code around it, and ordinary nesting stay well inside this.
    private const int MaximumIfDepth = 64;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ManyCandidatesNestShallowAndChooseTheirTarget(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("ManyCandidateDispatch", Source());
        // No final optimization: the module is the compiler's own, as wasm-opt receives it.
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.None));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, compilation.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.Equal(0, printed.ExitCode);
        Assert.InRange(MaximumIfNesting(printed.StandardOutput), 1, MaximumIfDepth);

        Assert.Equal(Expected() & 0x7fffffff, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    // The deepest run of ifs open at once in any function of the printed module.
    private static int MaximumIfNesting(string wat)
    {
        var deepest = 0;
        var open = new Stack<bool>();
        var ifs = 0;
        foreach (var line in wat.Split('\n'))
        {
            var text = line.TrimStart();
            if (text.StartsWith("(func ", StringComparison.Ordinal))
            {
                open.Clear();
                ifs = 0;
                continue;
            }
            var word = text.Split(' ', 2)[0];
            if (word is "block" or "loop" or "if" or "try_table" or "try")
            {
                open.Push(word == "if");
                if (word == "if") deepest = Math.Max(deepest, ++ifs);
            }
            else if (word == "end" && open.Count != 0 && open.Pop())
            {
                ifs--;
            }
        }
        return deepest;
    }

    // Shape i returns i when it overrides Value (every third does) and 1 otherwise; Add_i adds i; delegate type i,
    // combined with itself, runs twice and returns its last result, input + i.
    private static int Expected()
    {
        var total = 0L;
        for (var index = 0; index < Receivers; index++) total += index % 3 == 0 ? index : 1;
        for (var index = 0; index < Pointers; index++) total += 5 + index;
        for (var index = 0; index < Delegates; index++) total += 7 + index;
        return (int)total;
    }

    private static string Source()
    {
        var source = new StringBuilder("""
            using System;
            public class Shape { public virtual int Value() => 1; }
            public static unsafe class EntryPoint
            {
                public static int Run(int unused)
                {
                    var total = 0L;
                    foreach (var shape in Shapes())
                    {
                        Func<int> value = shape.Value;
                        total += value();
                    }
                    foreach (var add in Adds()) total += Call((delegate*<int, int>)add, 5);
                    total += Combined();
                    return (int)total & 0x7fffffff;
                }

                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                private static int Call(delegate*<int, int> add, int value) => add(value);

            """);
        source.AppendLine("    private static Shape[] Shapes() => [");
        for (var index = 0; index < Receivers; index++) source.AppendLine(CultureInfo.InvariantCulture, $"        new Shape{index}(),");
        source.AppendLine("    ];");
        // The pointers are kept as nint, as a table of native function addresses is.
        source.AppendLine("    private static nint[] Adds() => [");
        for (var index = 0; index < Pointers; index++) source.AppendLine(CultureInfo.InvariantCulture, $"        (nint)(delegate*<int, int>)&Add{index},");
        source.AppendLine("    ];");
        for (var index = 0; index < Pointers; index++)
            source.AppendLine(CultureInfo.InvariantCulture, $"    private static int Add{index}(int value) => value + {index};");
        source.AppendLine("    private static long Combined()");
        source.AppendLine("    {");
        source.AppendLine("        var total = 0L;");
        for (var index = 0; index < Delegates; index++)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"        {{ Step{index} step = static value => value + {index}; step += step; total += step(7); }}");
        }
        source.AppendLine("        return total;");
        source.AppendLine("    }");
        source.AppendLine("}");
        for (var index = 0; index < Receivers; index++)
        {
            source.AppendLine(index % 3 == 0
                ? string.Create(CultureInfo.InvariantCulture,
                    $"public sealed class Shape{index} : Shape {{ public override int Value() => {index}; }}")
                : string.Create(CultureInfo.InvariantCulture, $"public sealed class Shape{index} : Shape {{ }}"));
        }
        for (var index = 0; index < Delegates; index++) source.AppendLine(CultureInfo.InvariantCulture, $"public delegate int Step{index}(int value);");
        return source.ToString();
    }
}
