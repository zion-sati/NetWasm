// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConstructedGenericFactoryCacheCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerPreservesDistinctConstructedFactoriesThroughPublication(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace ConstructedGenericFactoryCacheFixture;

            public abstract class Instrument
            {
                protected Instrument(string name) => Name = name;

                public string Name { get; }
            }

            public sealed class Counter<T> : Instrument where T : struct
            {
                public Counter(string name) : base(name) { }
            }

            public sealed class Gauge<T> : Instrument where T : struct
            {
                public Gauge(string name) : base(name) { }
            }

            public sealed class Listener
            {
                public static Listener? Current { get; private set; }

                public Action<Instrument, Listener>? Published { get; set; }

                public int PublicationCount { get; private set; }

                public void Start() => Current = this;

                public void Enable(Instrument instrument) => PublicationCount += instrument.Name.Length > 0 ? 1 : 0;
            }

            public sealed class Meter
            {
                private readonly Dictionary<string, List<Instrument>> instruments = new();

                public Counter<T> CreateCounter<T>(string name) where T : struct =>
                    (Counter<T>)GetOrCreateInstrument<T>(typeof(Counter<T>), name, () => new Counter<T>(name));

                public Gauge<T> CreateGauge<T>(string name) where T : struct =>
                    (Gauge<T>)GetOrCreateInstrument<T>(typeof(Gauge<T>), name, () => new Gauge<T>(name));

                private Instrument GetOrCreateInstrument<T>(Type instrumentType, string name, Func<Instrument> create)
                    where T : struct
                {
                    if (instruments.TryGetValue(name, out var existing))
                    {
                        foreach (var instrument in existing)
                        {
                            if (instrument.GetType() == instrumentType)
                            {
                                return instrument;
                            }
                        }
                    }

                    var created = create();
                    if (existing is null)
                    {
                        existing = new List<Instrument>();
                        instruments.Add(name, existing);
                    }

                    existing.Add(created);
                    var listener = Listener.Current;
                    listener?.Published?.Invoke(created, listener);
                    return created;
                }
            }

            public static class EntryPoint
            {
                public static int Run(int scenario)
                {
                    try
                    {
                        var meter = new Meter();
                        Listener? listener = null;
                        if (scenario != 0)
                        {
                            listener = new Listener
                            {
                                Published = static (instrument, current) => current.Enable(instrument),
                            };
                            listener.Start();
                        }

                        var counter = meter.CreateCounter<int>("counter");
                        var gauge = meter.CreateGauge<int>("gauge");
                        if (counter.GetType() == gauge.GetType())
                        {
                            return 10 + scenario;
                        }

                        if (scenario != 0 && listener!.PublicationCount != 2)
                        {
                            return 20 + scenario;
                        }

                        return 0;
                    }
                    catch (InvalidCastException)
                    {
                        return 100 + scenario;
                    }
                }
            }
            """;

        var assembly = optimize
            ? assets.CompileOptimizedSource("ConstructedGenericFactoryCacheOptimized", source)
            : assets.CompileSource("ConstructedGenericFactoryCacheDebug", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConstructedGenericFactoryCacheFixture.EntryPoint",
            "Run",
            [],
            target));

        Assert.Equal(0, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
        Assert.Equal(0, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            1,
            target,
            result.StaticDataEnd));
    }
}
