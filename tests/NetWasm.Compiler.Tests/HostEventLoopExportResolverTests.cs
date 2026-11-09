using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class HostEventLoopExportResolverTests
{
    private static readonly AssemblyIdentity CoreLib = new("NetWasm.CoreLib");
    private static readonly EntityKey LoopType = new(CoreLib, 0x02000010);
    private static readonly MethodDefinitionModel Run = Method(0x06000100, "Run");
    private static readonly MethodDefinitionModel Callback = Method(0x06000101, "Callback",
        CliValueKind.I4, CliValueKind.I4, CliValueKind.I4);
    private static readonly MethodDefinitionModel Overload = Method(0x06000102, "Callback");
    private static readonly MethodDefinitionModel Elsewhere = new(
        new EntityKey(CoreLib, 0x06000103), new EntityKey(CoreLib, 0x02000011), "Run", true,
        new MethodSignatureModel(CliValueKind.I4, []), 0);

    [Fact]
    public void ExportsCoreLibsHostEventLoopFromAJavaScriptModule()
    {
        var exports = new HostEventLoopExportResolver().Resolve(Snapshot(), new NoMethods(), Options(true, CompilerEntryPointKind.Library));

        Assert.Equal(
            [new ProgramExport("netwasm.events.run", Run.Key), new ProgramExport("netwasm.events.callback", Callback.Key)],
            exports.Select(candidate => candidate.Export));
        Assert.All(exports, candidate => Assert.Null(candidate.DuplicateMethod));
    }

    [Theory]
    [InlineData(false, CompilerEntryPointKind.Library)]
    [InlineData(true, CompilerEntryPointKind.ManagedExecutable)]
    public void ExportsNothingFromAComponentOrAnExecutable(bool javaScriptHost, CompilerEntryPointKind kind)
    {
        Assert.Empty(new HostEventLoopExportResolver().Resolve(Snapshot(), new NoMethods(), Options(javaScriptHost, kind)));
    }

    [Fact]
    public void RequiresTheLoopAndItsInputs()
    {
        var missing = Assert.Throws<CompilerException>(() => new HostEventLoopExportResolver().Resolve(
            Snapshot() with { Types = [] }, new NoMethods(), Options(true, CompilerEntryPointKind.Library)));
        Assert.Equal(DiagnosticCode.InvalidEntryPoint, missing.Diagnostic.Code);
        var resolver = new HostEventLoopExportResolver();
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!, new NoMethods(), Options(true, CompilerEntryPointKind.Library)));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(Snapshot(), null!, Options(true, CompilerEntryPointKind.Library)));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(Snapshot(), new NoMethods(), null!));
    }

    private static MethodDefinitionModel Method(int token, string name, params CliValueKind[] parameters) => new(
        new EntityKey(CoreLib, token), LoopType, name, true,
        new MethodSignatureModel(CliValueKind.I4, [.. parameters]), 0);

    private static MetadataCompilationSnapshot Snapshot() => new(
        [],
        new AssemblyIdentity("App"),
        [
            new TypeDefinitionModel(LoopType, "System.Runtime.InteropServices", "HostEventLoop", false, [],
                [Run.Key, Callback.Key, Overload.Key]),
            new TypeDefinitionModel(new EntityKey(CoreLib, 0x02000011), "System.Runtime.InteropServices", "Other", false, [],
                [Elsewhere.Key]),
        ],
        [Elsewhere, Overload, Callback, Run],
        []);

    private static CompilerOptions Options(bool javaScriptHost, CompilerEntryPointKind kind) =>
        new("App.dll", [], "App", "Main", [])
        {
            UseJavaScriptExportBoundary = javaScriptHost,
            EntryPointKind = kind,
        };

    private sealed class NoMethods : IMethodFinder
    {
        public MethodDefinitionModel FindMethod(AssemblyIdentity assembly, string typeFullName, string methodName) =>
            throw new InvalidOperationException();

        public MethodDefinitionModel FindMethod(AssemblyIdentity assembly, int metadataToken) =>
            throw new InvalidOperationException();
    }
}
