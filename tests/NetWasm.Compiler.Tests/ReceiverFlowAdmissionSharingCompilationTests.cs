// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// The sites of one slot use whose receivers the flow bounds alike admit the same targets: they share one admitted set and
// one array of targets, as a whole program's 155,391 sites shared about 9,700 distinct sets, instead of each holding its
// own copy of what its bound admits.
public sealed class ReceiverFlowAdmissionSharingCompilationTests
{
    private const string Source = """
        using System.Runtime.CompilerServices;

        public abstract class Shape { public abstract int Area(); }
        public sealed class Square : Shape { public override int Area() => 4; }
        public sealed class Circle : Shape { public override int Area() => 3; }
        public sealed class Triangle : Shape { public override int Area() => 2; }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object[] Items(int input) =>
                input > 0 ? [new Square(), new Circle(), new Triangle()] : [new Triangle(), new Square(), new Circle()];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int First(int input) { Shape[] shapes = Unsafe.As<Shape[]>(Items(input)); return shapes[0].Area(); }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Second(int input) { Shape[] shapes = Unsafe.As<Shape[]>(Items(input)); return shapes[1].Area(); }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Third(int input) { Shape[] shapes = Unsafe.As<Shape[]>(Items(input)); return shapes[2].Area(); }

            public static int Run(int input) => First(input) * 100 + Second(input) * 10 + Third(input);
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void SitesASlotUseBoundsAlikeShareTheirTargets(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAdmissionSharingFixture", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));

        var sites = result.Program.DispatchCallSites.Values
            .Where(static site => site.Declaration.Definition.Name == "Area")
            .OrderBy(static site => site.Caller, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(3, sites.Length);
        Assert.All(sites, static site => Assert.True(site.BoundedTargets));
        Assert.Equal(["Circle", "Square", "Triangle"],
            sites[0].Targets.Select(static target => target.ReceiverType.FullName).Order(StringComparer.Ordinal));
        Assert.True(sites[0].Targets.Equals(sites[1].Targets) && sites[1].Targets.Equals(sites[2].Targets),
            "sites bounded alike hold separate copies of their targets");
    }
}
