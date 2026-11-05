using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ClsComplianceReferenceMetadataTests
{
    [Fact]
    public void StandardPublicContractsRemainClsCompliant()
    {
        using var assets = TestAssets.Create();

        try
        {
            _ = assets.CompileSourceWithCompiler(
                "ClsCompliantConsumer",
                """
                using System;
                using System.Globalization;
                using System.Linq.Expressions;
                using System.Threading;

                [assembly: CLSCompliant(true)]

                public sealed class PublicContract
                {
                    public Action<int> Callback { get; set; }

                    public CancellationToken CancellationToken { get; set; }

                    public CultureInfo Culture { get; set; }

                    public Expression<Func<int, int>> Selector { get; set; }
                }
                """,
                TestAssets.RepositorySdkVersion,
                "latest",
                warningsAsErrors: true);
        }
        catch (RoslynCompilationException error)
        {
            Assert.Fail(string.Join(",", error.DiagnosticCodes));
        }
    }

    [Fact]
    public void GenuineClsViolationsRemainDiagnostics()
    {
        using var assets = TestAssets.Create();

        var error = Assert.Throws<RoslynCompilationException>(() =>
            assets.CompileSourceWithCompiler(
                "ClsViolationConsumer",
                """
                using System;

                [assembly: CLSCompliant(true)]

                public sealed class PublicContract
                {
                    public uint Value { get; set; }
                }
                """,
                TestAssets.RepositorySdkVersion,
                "latest",
                warningsAsErrors: true));

        Assert.Contains("CS3003", error.DiagnosticCodes);
    }
}
