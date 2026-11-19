// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Symbols;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Tests.Symbols;

public sealed class CompilationSymbolSidecarBinderTests
{
    private static readonly string BuildId = new('c', 32);
    private static readonly RuntimeTypeSymbolTable Types = new(
        [
            new(1, "Object", "System", "System.Object", "System.Object"),
            new(2, "Exception", "System", "System.Exception", "System.Exception"),
        ],
        [2],
        []);

    // Every build names its exception types, which a host's crash report names (issue 225).
    [Fact]
    public void WithoutTypeNamesTheSidecarNamesOnlyTheExceptionTypes()
    {
        var result = Bind(typeNames: false, stackTraces: false);

        var entry = Assert.Single(SymbolSidecarContents.Read(result.TypeSymbols!.Bytes).Entries);
        Assert.Equal(2, entry.Id);
        Assert.Equal(BuildId, SymbolSidecarContents.Read(result.TypeSymbols.Bytes).BuildId);
        Assert.Null(result.MethodSymbols);
    }

    [Fact]
    public void TypeNamesNameEveryTypeAndStackTracesWriteTheMethodSymbols()
    {
        var result = Bind(typeNames: true, stackTraces: true);

        Assert.Equal([1, 2], SymbolSidecarContents.Read(result.TypeSymbols!.Bytes).Entries.Select(entry => entry.Id));
        var method = Assert.Single(SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Entries);
        Assert.Equal((5, "App.Program::Main"), (method.Id, method.Strings[0]));
    }

    [Fact]
    public void AStackTraceCompileWithoutSymbolsWritesAnEmptyMethodSidecar() =>
        Assert.Empty(SymbolSidecarContents.Read(new CompilationSymbolSidecarBinder(new SymbolSidecarWriter())
            .Bind(Result(), Types, BuildId, default, typeNames: false, stackTraces: true).MethodSymbols!.Bytes).Entries);

    [Fact]
    public void RejectsMissingInputs()
    {
        var binder = new CompilationSymbolSidecarBinder(new SymbolSidecarWriter());

        Assert.Throws<ArgumentNullException>(() => new CompilationSymbolSidecarBinder(null!));
        Assert.Throws<ArgumentNullException>(() => binder.Bind(null!, Types, BuildId, [], false, false));
        Assert.Throws<ArgumentNullException>(() => binder.Bind(Result(), null!, BuildId, [], false, false));
    }

    private static CompilationResult Bind(bool typeNames, bool stackTraces) =>
        new CompilationSymbolSidecarBinder(new SymbolSidecarWriter()).Bind(
            Result(), Types, BuildId, [new WasmStackTraceSymbol(5, "App.Program::Main")], typeNames, stackTraces);

    private static CompilationResult Result() => new([], null!, null!, null!, 0);

}
