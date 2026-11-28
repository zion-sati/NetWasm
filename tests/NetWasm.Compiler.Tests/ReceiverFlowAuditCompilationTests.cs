// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// The receiver flow audit (RECEIVER_TYPE_FLOW_DESIGN.md, step B): every dispatched call the analysis follows
// precisely checks its receiver against the analysis's set, and every call whose targets a bound admitted against its
// targets' receivers, and fails fast outside them, dispatch unchanged. Programs whose receivers reach their calls
// through each source kind, and LINQ's iterator chains, run as without it; a bound too tight fails.
public sealed class ReceiverFlowAuditCompilationTests
{
    private const string Linq = """
        using System.Collections.Generic;
        using System.Linq;

        public sealed class Named { public Named(string name) { Name = name; } public string Name { get; } }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var numbers = Enumerable.Range(0, input + 5).Where(x => x % 2 == 0).Select(x => x * 3).ToList();
                var names = numbers.Select(x => x.ToString()).Select(s => new Named(s)).Where(n => n.Name.Length < 3);
                var grouped = names.GroupBy(n => n.Name.Length).OrderBy(g => g.Key).Select(g => g.Count()).ToArray();
                var set = new HashSet<long>(numbers.Select(x => (long)x));
                return grouped.Sum() * 100 + set.Count + new[] { 1.5, 2.5 }.Select(d => (int)d).Sum();
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AuditedSourceKindsRunAsWithoutTheAudit(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowSourceKindFixture", ReceiverFlowSourceKindCompilationTests.Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        Assert.Equal(22, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, target,
            result.StaticDataEnd));
    }

    // Type objects and handles the runtime makes (GetType, typeof, Enum's names) and a field set through a cached
    // delegate's branch and Interlocked.CompareExchange, whose address crosses blocks: the audit failed at each before
    // the analysis treated compiler-only instructions, handle fields and addresses across blocks as unknown.
    private const string RuntimeMade = """
        using System;
        using System.Threading;

        public enum Color { Red, Green, Blue }
        public enum Wide : long { A = 1, B = 1L << 40 }
        public abstract class Greeter { public abstract int Greet(); }
        public sealed class Hello : Greeter
        {
            private readonly Func<int, int> _twice;
            public Hello(Func<int, int> twice) { _twice = twice; }
            public override int Greet() => _twice(5);
        }

        public static class EntryPoint
        {
            private static Greeter? s_greeter;

            private static Greeter Greeter()
            {
                if (s_greeter == null) Interlocked.CompareExchange(ref s_greeter, new Hello(static x => x * 2), null);
                return s_greeter!;
            }

            public static int Run(int input)
            {
                var total = 0;
                total += Enum.GetName(typeof(Color), Color.Green)!.Length;
                total += Enum.GetName(typeof(Color), input - 2)!.Length;
                total += Enum.GetName(typeof(Wide), Wide.B)!.Length;
                total += Enum.GetName(Color.Blue)!.Length;
                total += ((Color)input).ToString().Length;
                total += typeof(Hello).ToString().Length > 0 ? 100 : 0;
                total += new Hello(x => x).GetType() == typeof(Hello) ? 1000 : 0;
                return total + Greeter().Greet() * 10000;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AuditedRuntimeMadeObjectsRunAsWithoutTheAudit(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAuditRuntimeMadeFixture", RuntimeMade);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        // Green 5, Blue 4 (input - 2 = 1 is Green: 5), B 1, Blue 4, Blue 4 (input 3 is not a name: "3", 1).
        Assert.Equal(5 + 5 + 1 + 4 + 1 + 100 + 1000 + 10 * 10000, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 3, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AuditedLinqRunsAsWithoutTheAudit(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAuditLinqFixture", Linq);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        // input 3: 0, 6, 12, 18 as strings, all shorter than 3: four names in two lengths; four longs; 1 + 2.
        Assert.Equal(4 * 100 + 4 + 3, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, target,
            result.StaticDataEnd));
    }

    // Unsafe.As does not check its claim, so a local declared Label[] holding Bigs bounds its elements by Label: a call
    // on an element takes Label's targets only, and without the audit dispatch's unchecked last case would run Label's
    // ToString on a Big. The audit fails where the Big arrives.
    private const string TooTightBound = """
        using System.Runtime.CompilerServices;

        public sealed class Big { public override string ToString() => "big"; }

        public sealed class Label { public override string ToString() => "label"; }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object[] Items(int input) => [input > 0 ? new Big() : new Label()];

            public static int Run(int input)
            {
                Label[] labels = Unsafe.As<Label[]>(Items(input));
                object element = labels[0];
                return element.ToString()!.Length;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AReceiverOutsideABoundedCallsTargetsFailsTheAudit(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAuditTooTightBoundFixture", TooTightBound);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        var site = Assert.Single(result.Program.DispatchCallSites.Values, static site =>
            site.Caller.Contains("EntryPoint", StringComparison.Ordinal) && site.Declaration.Definition.Name == "ToString");
        Assert.True(site.BoundedTargets);
        Assert.Equal(["Label"], site.AuditedReceivers.Select(static type => type.FullName));
        var failure = Assert.ThrowsAny<Exception>(() => ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 1, target, result.StaticDataEnd));
        Assert.Contains("a receiver outside the bounded call's targets", failure.Message, StringComparison.Ordinal);
    }

    // A bounded call with as many targets as SharedDispatchPolicy shares calls its declaration's shared dispatch
    // function, which serves every sharing call's targets: the call checks its own audited receivers before it. The
    // Unsafe.As claim bounds the element by Many, whose 32 subclasses are the call's targets; the Outside that arrives
    // fails the audit.
    private static string SharedDispatchTooTightBound()
    {
        var source = new System.Text.StringBuilder("""
            using System.Runtime.CompilerServices;

            public abstract class Base { public abstract int Id(); }

            public abstract class Many : Base { }

            public sealed class Outside : Base { public override int Id() => 99; }

            """);
        for (var index = 0; index < SharedDispatchPolicy.MinimumTargets; index++)
        {
            source.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"public sealed class M{index} : Many {{ public override int Id() => {index}; }}");
        }
        source.AppendLine("""
            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static Base Make(int input) => input switch
                {
            """);
        for (var index = 0; index < SharedDispatchPolicy.MinimumTargets; index++)
        {
            source.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"        {index + 2} => new M{index}(),");
        }
        source.AppendLine("""
                    _ => new Outside(),
                };

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static object[] Items(int input) => [Make(input)];

                public static int Run(int input)
                {
                    Many[] many = Unsafe.As<Many[]>(Items(input));
                    Base element = many[0];
                    return element.Id();
                }
            }
            """);
        return source.ToString();
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AReceiverOutsideASharedDispatchCallsTargetsFailsTheAudit(WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAuditSharedDispatchFixture", SharedDispatchTooTightBound());
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        var site = Assert.Single(result.Program.DispatchCallSites.Values, static site =>
            site.Caller.Contains("EntryPoint", StringComparison.Ordinal) && site.Declaration.Definition.Name == "Id");
        Assert.True(site.BoundedTargets);
        Assert.Equal(SharedDispatchPolicy.MinimumTargets, site.Targets.Length);
        Assert.Equal(SharedDispatchPolicy.MinimumTargets, site.AuditedReceivers.Length);
        Assert.Equal(7, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 9, target,
            result.StaticDataEnd));
        var failure = Assert.ThrowsAny<Exception>(() => ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 1, target, result.StaticDataEnd));
        Assert.Contains("a receiver outside the bounded call's targets", failure.Message, StringComparison.Ordinal);
    }

    // ldvirtftn picks the function as a call picks its target, but the receiver flow takes a function load as a boundary
    // and does not narrow it: its targets are every allocated type's, so the false claim that bounds the element by
    // Label cannot misdirect it, and a Big gets Big's ToString. Were a load narrowed, it would carry audited receivers,
    // which the load checks as a call does (CallableLoadingEmitterTests).
    private const string TooTightFunctionLoad = """
        using System;
        using System.Runtime.CompilerServices;

        public sealed class Big { public override string ToString() => "big"; }

        public sealed class Label { public override string ToString() => "label"; }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object[] Items(int input) => [input > 0 ? new Big() : new Label()];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Length(Func<string> text) => text().Length;

            public static int Run(int input)
            {
                Label[] labels = Unsafe.As<Label[]>(Items(input));
                object element = labels[0];
                return Length(element.ToString);
            }
        }
        """;

    // A bound that admits no allocated type leaves a call no target; in an audit build it keeps its dispatch, so the
    // Circle the false claim bounds by Square (which the program never makes) fails the audit, where other builds
    // would throw the receiver as a call nothing implements.
    private const string BoundWithoutTargets = """
        using System.Runtime.CompilerServices;

        public abstract class Shape { public abstract int Sides(); }

        public sealed class Square : Shape { public override int Sides() => 4; }

        public sealed class Circle : Shape { public override int Sides() => 0; }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object[] Items(int input) => [new Circle()];

            public static int Run(int input)
            {
                Square[] squares = Unsafe.As<Square[]>(Items(input));
                Shape shape = squares[0];
                return shape.Sides();
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AFunctionLoadIsNotNarrowedSoAFalseBoundCannotMisdirectIt(WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAuditFunctionLoadFixture", TooTightFunctionLoad);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        var site = Assert.Single(result.Program.DispatchCallSites.Values, static site =>
            site.Caller.Contains("EntryPoint", StringComparison.Ordinal) && site.Declaration.Definition.Name == "ToString");
        Assert.False(site.BoundedTargets);
        Assert.False(site.PreciseTargets);
        Assert.Contains(site.Targets, static target => target.ReceiverType.FullName == "Big");
        Assert.Equal(5, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target,
            result.StaticDataEnd));
        Assert.Equal(3, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 1, target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AReceiverABoundWithoutTargetsMissedFailsTheAudit(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAuditBoundWithoutTargetsFixture", BoundWithoutTargets);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        var site = Assert.Single(result.Program.DispatchCallSites.Values, static site =>
            site.Caller.Contains("EntryPoint", StringComparison.Ordinal) && site.Declaration.Definition.Name == "Sides");
        Assert.True(site.BoundedTargets);
        Assert.Empty(site.Targets);
        Assert.Empty(site.AuditedReceivers);
        var failure = Assert.ThrowsAny<Exception>(() => ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 1, target, result.StaticDataEnd));
        Assert.Contains("a receiver outside the bounded call's targets", failure.Message, StringComparison.Ordinal);
    }
}
