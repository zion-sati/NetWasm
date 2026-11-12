using System.Buffers.Binary;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class SymbolSidecarsTests
{
    private static readonly byte[] TypeDigest = [1, 2, 3];

    // A build ID is a function of the names alone: deterministic (#187), and any name change changes it.
    [Fact]
    public void TheBuildIdFollowsTheTypeAndMethodNames()
    {
        var calculator = new SymbolBuildIdCalculator();
        var id = calculator.Calculate([.. TypeDigest], [new(1, "App::Main")]);

        Assert.Equal(32, id.Length);
        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.Equal(id, calculator.Calculate([.. TypeDigest], [new(1, "App::Main")]));
        Assert.NotEqual(id, calculator.Calculate([1, 2, 4], [new(1, "App::Main")]));
        Assert.NotEqual(id, calculator.Calculate([.. TypeDigest], [new(2, "App::Main")]));
        Assert.NotEqual(id, calculator.Calculate([.. TypeDigest], [new(1, "App::Run")]));
        Assert.Equal(calculator.Calculate([], []), calculator.Calculate(default, default));
    }

    [Fact]
    public void TheDescriptorEncodesFlagsBuildIdAndProgramName()
    {
        var buildId = new string('d', 32);

        var bytes = new SidecarDescriptor(4000, SidecarDescriptor.TypeSymbolsFlag, buildId, "App.wasm").Encode();

        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes));
        Assert.Equal(32, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(buildId, System.Text.Encoding.Unicode.GetString(bytes, 8, 64));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(72)));
        Assert.Equal("App.wasm", System.Text.Encoding.Unicode.GetString(bytes, 76, 16));
        Assert.Equal(92, bytes.Length);
    }
}
