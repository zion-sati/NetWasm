// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// The receiver flow analysis follows a generic virtual call's receiver through each kind of source
// (RECEIVER_TYPE_FLOW_DESIGN.md, 4.2 and 4.3). Three shapes are allocated; each call below receives some of them through
// one source kind, and its result type names the site in the report. A source the analysis does not follow (an array
// element, an address passed out) leaves the call top, with every shape its declared type admits.
public sealed class ReceiverFlowSourceKindCompilationTests
{
    internal const string Source = """
        using System;

        public interface IVisitor<T> { T Circle(); T Square(); T Triangle(); }
        public abstract class Shape { public abstract T Accept<T>(IVisitor<T> visitor); }
        public sealed class Circle : Shape { public override T Accept<T>(IVisitor<T> visitor) => visitor.Circle(); }
        public sealed class Square : Shape { public override T Accept<T>(IVisitor<T> visitor) => visitor.Square(); }
        public sealed class Triangle : Shape { public override T Accept<T>(IVisitor<T> visitor) => visitor.Triangle(); }

        public sealed class R<TTag> { public readonly int Value; public R(int value) { Value = value; } }
        public sealed class Visitor<TTag> : IVisitor<R<TTag>>
        {
            public R<TTag> Circle() => new(1);
            public R<TTag> Square() => new(2);
            public R<TTag> Triangle() => new(3);
        }
        public sealed class ToSquare : IVisitor<Shape>
        {
            public Shape Circle() => new Square();
            public Shape Square() => new Square();
            public Shape Triangle() => new Square();
        }

        public sealed class Local; public sealed class Parameter; public sealed class InstanceField;
        public sealed class StaticField; public sealed class Property; public sealed class InterfaceReturn;
        public sealed class LambdaParameter; public sealed class Closure; public sealed class ArrayElement;
        public sealed class Combined; public sealed class Chained; public sealed class GenericReceiver;
        public sealed class Cast; public sealed class Join; public sealed class OutParameter;

        public interface ISource { Shape Get(); }
        public sealed class CircleSource : ISource { public Shape Get() => new Circle(); }

        public sealed class Holder
        {
            public Shape Field;
            public Shape Auto { get; set; }
            public static Shape Static = new Square();
            public Holder(Shape shape) { Field = shape; Auto = shape; }
        }

        public static class EntryPoint
        {
            private static int ThroughParameter(Shape shape) => shape.Accept(new Visitor<Parameter>()).Value;

            private static int Generic<TShape>(TShape shape) where TShape : Shape => shape.Accept(new Visitor<GenericReceiver>()).Value;

            private static void Make(out Shape shape) => shape = new Circle();

            public static int Run(int input)
            {
                var total = 0;
                Shape local = new Circle();
                total += local.Accept(new Visitor<Local>()).Value;
                total += ThroughParameter(new Square());
                var holder = new Holder(new Circle());
                total += holder.Field.Accept(new Visitor<InstanceField>()).Value;
                total += Holder.Static.Accept(new Visitor<StaticField>()).Value;
                total += holder.Auto.Accept(new Visitor<Property>()).Value;
                ISource source = new CircleSource();
                total += source.Get().Accept(new Visitor<InterfaceReturn>()).Value;
                Func<Shape, int> lambda = shape => shape.Accept(new Visitor<LambdaParameter>()).Value;
                total += lambda(new Square());
                Shape captured = new Circle();
                Func<int> closure = () => captured.Accept(new Visitor<Closure>()).Value;
                total += closure();
                var shapes = new Shape[] { new Triangle(), new Circle() };
                total += shapes[input & 1].Accept(new Visitor<ArrayElement>()).Value;
                Func<Shape> first = () => new Circle();
                Func<Shape> second = () => new Triangle();
                var both = (Func<Shape>)Delegate.Combine(first, second);
                total += both().Accept(new Visitor<Combined>()).Value;
                total += new Triangle().Accept(new ToSquare()).Accept(new Visitor<Chained>()).Value;
                total += Generic(new Circle());
                object boxed = new Square();
                total += ((Shape)boxed).Accept(new Visitor<Cast>()).Value;
                Shape joined = input > 2 ? new Circle() : new Square();
                total += joined.Accept(new Visitor<Join>()).Value;
                Make(out var made);
                total += made.Accept(new Visitor<OutParameter>()).Value;
                return total;
            }
        }
        """;

    public static TheoryData<string, bool, int> Sites => new()
    {
        { "Local", true, 1 },
        { "Parameter", true, 1 },
        { "InstanceField", true, 1 },
        { "StaticField", true, 1 },
        { "Property", true, 1 },
        { "InterfaceReturn", true, 1 },
        { "LambdaParameter", true, 1 },
        { "Closure", true, 1 },
        // Array elements are not followed: every shape.
        { "ArrayElement", false, 3 },
        // A combined delegate invokes both targets.
        { "Combined", true, 2 },
        // A generic virtual call's result is its targets' returns.
        { "Chained", true, 1 },
        { "GenericReceiver", true, 1 },
        { "Cast", true, 1 },
        // Both arms of a conditional reach the join.
        { "Join", true, 2 },
        // An address passed out may be written where the analysis does not see.
        { "OutParameter", false, 3 },
    };

    [Theory]
    [MemberData(nameof(Sites))]
    public void ACallsReceiversFollowTheirSource(string site, bool precise, int targets)
    {
        var report = Report();
        Assert.True(report.TryGetValue(site, out var found), $"no generic virtual site for {site}");
        Assert.True((precise, targets) == (found.Precise, found.Targets),
            $"{site}: expected ({precise}, {targets}), found ({found.Precise}, {found.Targets}) {found.Detail}");
    }

    private static Dictionary<string, (bool Precise, int Targets, string Detail)>? s_report;
    private static readonly object Gate = new();

    // One compile serves every case: the sites of R<tag> by tag.
    private static Dictionary<string, (bool Precise, int Targets, string Detail)> Report()
    {
        lock (Gate)
        {
            if (s_report is not null) return s_report;
            using var assets = TestAssets.Create();
            var assembly = assets.CompileSource("ReceiverFlowSourceKindFixture", Source);
            var trace = Path.Combine(assets.Directory, "receiver-flow.jsonl");
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly, [assets.CoreLib], "EntryPoint", "Run", [], DiagnosticTracePath: trace,
                Optimization: FinalWasmOptimization.None));
            // Circle 1, Square 2, Circle 1, Square 2, Circle 1, Circle 1, Square 2, Circle 1, Circle 1 (input 3),
            // Triangle 3 (the combined delegate returns its last target's), Square 2, Circle 1, Square 2, Circle 1,
            // Circle 1.
            Assert.Equal(22, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3,
                WasmTarget.Wasm32, result.StaticDataEnd));
            using var document = JsonDocument.Parse(File.ReadAllText(trace + ".passes/03-receiver-flow.json"));
            s_report = document.RootElement.GetProperty("genericVirtualSites").EnumerateArray()
                .Select(static site => (Declaration: site.GetProperty("declaration").GetString()!,
                    Precise: site.GetProperty("precise").GetBoolean(), Targets: site.GetProperty("targetsByFlow").GetInt32(),
                    Detail: $"cause {site.GetProperty("cause")}, receivers {site.GetProperty("receivers")}"))
                .Where(static site => site.Declaration.Contains("::", System.StringComparison.Ordinal) &&
                    site.Declaration.Contains("R`1<[ReceiverFlowSourceKindFixture]", System.StringComparison.Ordinal))
                .ToDictionary(static site => Tag(site.Declaration), static site => (site.Precise, site.Targets, site.Detail));
            return s_report;
        }
    }

    // The tag type R<tag> names: the last fixture type in the declaration.
    private static string Tag(string declaration)
    {
        const string marker = "R`1<[ReceiverFlowSourceKindFixture]";
        var start = declaration.LastIndexOf(marker, System.StringComparison.Ordinal) + marker.Length;
        return declaration[start..declaration.IndexOf('>', start)];
    }
}
