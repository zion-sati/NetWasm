using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class OrdinaryExceptionContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OrdinaryExceptionContractsPreserveErrorData(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "OrdinaryExceptionFixture", Source, "OrdinaryExceptionFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Diagnostics.Tracing;
        using System.Globalization;
        using System.Reflection;
        using System.Resources;
        using System.Runtime.Serialization;
        namespace OrdinaryExceptionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var inner = new Exception("inner");
                Exception[][] groups = {
                    new Exception[] { new EventSourceException(), new EventSourceException("m"), new EventSourceException("m", inner) },
                    new Exception[] { new InvalidFilterCriteriaException(), new InvalidFilterCriteriaException("m"), new InvalidFilterCriteriaException("m", inner) },
                    new Exception[] { new TargetException(), new TargetException("m"), new TargetException("m", inner) },
                    new Exception[] { new MissingManifestResourceException(), new MissingManifestResourceException("m"), new MissingManifestResourceException("m", inner) },
                    new Exception[] { new MissingSatelliteAssemblyException(), new MissingSatelliteAssemblyException("m"), new MissingSatelliteAssemblyException("m", inner) },
                    new Exception[] { new SerializationException(), new SerializationException("m"), new SerializationException("m", inner) }
                };
                int[] codes = { unchecked((int)0x80131500), unchecked((int)0x80131601), unchecked((int)0x80131603),
                    unchecked((int)0x80131532), unchecked((int)0x80131536), unchecked((int)0x8013150c) };
                for (var i = 0; i < groups.Length; i++)
                {
                    for (var j = 0; j < groups[i].Length; j++)
                    {
                        var error = groups[i][j];
                        if (error.HResult != codes[i] || string.IsNullOrEmpty(error.Message)) return 1;
                        if (j != 0 && error.Message != "m") return 2;
                        if (!ReferenceEquals(error.InnerException, j == 2 ? inner : null)) return 3;
                    }
                }
                var satellite = new MissingSatelliteAssemblyException("missing", "xy");
                if (satellite.CultureName != "xy" || satellite.Message != "missing" || satellite.InnerException != null) return 4;
                if (new MissingSatelliteAssemblyException().CultureName != null) return 5;
                var cultures = new[] {
                    new CultureNotFoundException(), new CultureNotFoundException("m"),
                    new CultureNotFoundException("p", "m"), new CultureNotFoundException("m", inner),
                    new CultureNotFoundException("p", "bad", "m"), new CultureNotFoundException("m", "bad", inner),
                    new CultureNotFoundException("m", 1234, inner), new CultureNotFoundException("p", 1234, "m") };
                foreach (var error in cultures)
                    if (error.HResult != unchecked((int)0x80070057) || error is not ArgumentException) return 6;
                if (cultures[0].Message != "Culture is not supported." || cultures[1].Message != "m") return 7;
                if (cultures[2].ParamName != "p" || !ReferenceEquals(cultures[3].InnerException, inner)) return 8;
                if (cultures[4].InvalidCultureName != "bad" || cultures[4].InvalidCultureId != null) return 9;
                if (cultures[4].Message != "m (Parameter 'p')\nbad is an invalid culture identifier.") return 10;
                if (cultures[5].InvalidCultureName != "bad" || !ReferenceEquals(cultures[5].InnerException, inner)) return 11;
                if (cultures[6].InvalidCultureId != 1234 || cultures[6].InvalidCultureName != null || !ReferenceEquals(cultures[6].InnerException, inner)) return 12;
                if (cultures[7].Message != "m (Parameter 'p')\n1234 (0x04d2) is an invalid culture identifier.") return 13;
                var empty = new ReflectionTypeLoadException(null, null);
                if (empty.Types.Length != 0 || empty.LoaderExceptions.Length != 0) return 14;
                Type[] types = { typeof(int), null, typeof(string) };
                Exception[] errors = { new Exception("first"), null, new Exception("last") };
                var load = new ReflectionTypeLoadException(types, errors, "load");
                if (!ReferenceEquals(load.Types, types) || !ReferenceEquals(load.LoaderExceptions, errors)) return 15;
                if (load.Message != "load\nfirst\nlast" || load.HResult != unchecked((int)0x80131602)) return 16;
                if (!load.ToString().Contains("first") || !load.ToString().Contains("last")) return 17;
                try { throw satellite; } catch (MissingSatelliteAssemblyException e) { if (e.CultureName != "xy") return 18; }
                try { throw load; } catch (SystemException e) { if (!ReferenceEquals(e, load)) return 19; }
                try { throw groups[2][1]; } catch (ApplicationException e) { if (e.Message != "m") return 20; }
                return input + 1;
            }
        }
        """;
}
