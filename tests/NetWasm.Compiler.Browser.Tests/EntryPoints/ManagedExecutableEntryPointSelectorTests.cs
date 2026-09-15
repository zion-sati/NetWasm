using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Metadata.ManagedExecutables;
using SyncMarker = NetWasm.Compiler.Tasks.Tests.Fixtures.ManagedExecutable.Marker;
using AsyncMarker = NetWasm.Compiler.Tasks.Tests.Fixtures.AsyncStringArgumentsManagedExecutable.Marker;

namespace NetWasm.Compiler.Browser.Tests.EntryPoints;

public sealed class ManagedExecutableEntryPointSelectorTests
{
    [Fact]
    public void SelectsSynchronousMainFromVirtualBytesWithoutOpeningTheNamedPath()
    {
        var entry = new ManagedExecutableEntryPointSelector().SelectEntryPoint("virtual/absent.dll",
            File.ReadAllBytes(typeof(SyncMarker).Assembly.Location));

        Assert.Equal("Container+Program", entry.TypeName);
        Assert.Equal("Main", entry.MethodName);
        Assert.Equal(new ManagedExecutableEntryPointAbi(ManagedExecutableParameterShape.None,
            ManagedExecutableReturnShape.ExitCode), entry.Abi);
    }

    [Fact]
    public void SelectsOriginalAsyncMainAndReadsItsActualExplicitTokenAbi()
    {
        var image = File.ReadAllBytes(typeof(AsyncMarker).Assembly.Location);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var wrapperToken = pe.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress;
        var selector = new ManagedExecutableEntryPointSelector();

        var selected = selector.SelectEntryPoint("virtual/async.dll", image);
        var explicitEntry = selector.SelectEntryPoint("virtual/async.dll", image, selected.MetadataToken);
        var explicitWrapper = selector.SelectEntryPoint("virtual/async.dll", image, wrapperToken);

        Assert.NotEqual(wrapperToken, selected.MetadataToken);
        Assert.Equal("Main", selected.MethodName);
        Assert.Equal(new ManagedExecutableEntryPointAbi(ManagedExecutableParameterShape.StringArray,
            ManagedExecutableReturnShape.ExitCode, ManagedExecutableCompletionShape.Asynchronous), selected.Abi);
        Assert.Equal(selected, explicitEntry);
        Assert.Equal("<Main>", explicitWrapper.MethodName);
        Assert.Equal(ManagedExecutableCompletionShape.Synchronous, explicitWrapper.Abi.CompletionShape);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x02000001)]
    public void RejectsAnExplicitTokenThatIsNotAMethod(int token)
    {
        var failure = Assert.Throws<CompilerException>(() => new ManagedExecutableEntryPointSelector().SelectEntryPoint(
            "virtual/app.dll", File.ReadAllBytes(typeof(SyncMarker).Assembly.Location), token));
        Assert.Equal(DiagnosticCode.InvalidEntryPoint, failure.Diagnostic.Code);
    }

    [Fact]
    public void MalformedVirtualPePreservesCompilerDiagnostic()
    {
        var failure = Assert.Throws<CompilerException>(() =>
            new ManagedExecutableEntryPointSelector().SelectEntryPoint("virtual/bad.dll", new byte[] { 1, 2, 3 }));
        Assert.Equal(DiagnosticCode.InvalidEntryPoint, failure.Diagnostic.Code);
    }
}
