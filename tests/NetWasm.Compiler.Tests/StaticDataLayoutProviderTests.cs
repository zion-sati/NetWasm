using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class StaticDataLayoutProviderTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PublishesStaticFactsAndRejectsUnassignedStrings(WasmTarget target)
    {
        var layout = new StringLayout(64, 5, 8);
        var segment = new DataSegment(64, [1, 2, 3]);
        var data = new ManagedStaticData(256, [], [],
            ImmutableDictionary<string, StringLayout>.Empty.Add("hello", layout),
            [], [segment], [], [], [], [32])
        {
            TypeTableAddress = 128,
        };
        var factory = new StaticDataLayoutProviderFactory();
        var provider = ((IStaticDataLayoutProviderFactory)factory).Create(
            MemberDescriptorLayoutProviderTests.Snapshot(target, data));

        Assert.Equal(layout, provider.GetStringLayout("hello"));
        Assert.Equal(256, provider.StaticDataEnd);
        Assert.Equal(32, Assert.Single(provider.StaticRootAddresses));
        Assert.Equal(segment, Assert.Single(provider.DataSegments));
        Assert.Equal(128, provider.TypeTableAddress);
        Assert.IsNotAssignableFrom<IMemberDescriptorLayout>(provider);
        var error = Assert.Throws<CompilerException>(() =>
            provider.GetStringLayout("missing"));
        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMissingSnapshotBeforePublishingAProvider()
    {
        var factory = new StaticDataLayoutProviderFactory();

        var error = Assert.Throws<ArgumentNullException>(() =>
            ((IStaticDataLayoutProviderFactory)factory).Create(null!));

        Assert.Equal("snapshot", error.ParamName);
    }
}
