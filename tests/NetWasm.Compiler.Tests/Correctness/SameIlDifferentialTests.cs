using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class SameIlDifferentialTests
{
    [Fact]
    public void ExecutesTheExactDesktopPeThroughCoreClrAndNetWasm()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();
        runner.Run(new(
            "SameIlArithmeticControlFlow",
            "NetWasm.Correctness.SameIl",
            """
            namespace NetWasm.Correctness.SameIl;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    var value = input;
                    for (var index = 0; index < 4; index++)
                    {
                        value = (value * 3) + index;
                        if ((value & 1) == 0)
                        {
                            value /= 2;
                        }
                        else
                        {
                            value -= index;
                        }
                    }
                    _trace = value;
                    return input switch
                    {
                        < 0 => value - 7,
                        0 => value,
                        _ => value + 11,
                    };
                }

                public static int Trace() => _trace;
            }
            """,
            [-7, -1, 0, 1, 19])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            ExecuteWasm64 = true,
        });
    }

    [Fact]
    public void ExecutesObjectsArraysAndExceptionHandlingFromTheExactDesktopPe()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();
        runner.Run(new(
            "SameIlObjectsArraysEh",
            "NetWasm.Correctness.SameIlObjects",
            """
            namespace NetWasm.Correctness.SameIlObjects;

            public sealed class ProbeException : System.Exception
            {
            }

            public sealed class Box
            {
                public int Value;
            }

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    var values = new int[] { input, 2, 5 };
                    var box = new Box { Value = values[0] + values[2] };
                    try
                    {
                        if (input < 0)
                        {
                            throw new ProbeException();
                        }
                        return box.Value + values.Length;
                    }
                    catch (ProbeException)
                    {
                        return box.Value - values[1];
                    }
                    finally
                    {
                        _trace = box.Value * 10 + values[1];
                    }
                }

                public static int Trace() => _trace;
            }
            """,
            [-4, 0, 9])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
        });
    }

    [Fact]
    public void UncheckedOutOfRangeFloatingConversionDoesNotTrap()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();
        runner.Run(new(
            "UncheckedFloatingConversion",
            "NetWasm.Correctness.FloatingConversion",
            """
            namespace NetWasm.Correctness.FloatingConversion;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var scaled = (double)input * 1e300;
                    return (int)scaled;
                }

                public static int Trace() => 0;
            }
            """,
            [int.MinValue, -1, 0, 1, int.MaxValue])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            ExecuteWasm64 = true,
        });
    }

    [Fact]
    public void CompilerRecordsOneByteIdenticalArtifactForSameIlMode()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var fixture = CorrectnessTestAssets.CreateFixture("SameIlArtifact") with
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
        };
        var compilation = new RoslynCorpusCompiler(
            environment,
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier())).Compile(
                fixture,
                CilProfile.Release,
                CorrectnessTestAssets.CreateDirectory());

        Assert.Equal(
            compilation.Desktop.AssemblyPath,
            compilation.NetWasm.AssemblyPath);
        Assert.Equal(
            compilation.Desktop.AssemblySha256,
            compilation.NetWasm.AssemblySha256);
    }

    [Fact]
    public void SameSourceModeRequiresAnExplicitReason()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var fixture = CorrectnessTestAssets.CreateFixture("MissingSameSourceReason")
            with
        { SameSourceReason = null };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RoslynCorpusCompiler(
                environment,
                new QualifiedProcessRunner(),
                CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()))
                .Compile(
                    fixture,
                    CilProfile.Release,
                    CorrectnessTestAssets.CreateDirectory()));

        Assert.Contains("must explain", exception.Message);
    }
}
