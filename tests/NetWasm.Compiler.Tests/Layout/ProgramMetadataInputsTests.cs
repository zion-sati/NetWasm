// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Layout;

// The program's assembly names and manifest resources come from the compilation's loaded assemblies.
public sealed class ProgramMetadataInputsTests
{
    private static readonly AssemblyIdentity Assembly = new("Inputs");

    [Fact]
    public void AnAssemblyTheCompilationDidNotLoadHasNoDisplayName()
    {
        var names = new ProgramAssemblyNames(new MetadataCompilationSnapshot([], Assembly, [], [], []));

        var exception = Assert.Throws<CompilerException>(() => names.DisplayName(new("Missing")));

        Assert.Contains("the program assembly 'Missing' was not loaded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresTheirInputs()
    {
        var metadata = new MetadataCompilationSnapshot([], Assembly, [], [], []);
        var program = new ReachableProgram(null, [], [], [], [], [], [], [], [], [], []);

        Assert.Throws<ArgumentNullException>(() => new ProgramAssemblyNames(null!));
        Assert.Throws<ArgumentNullException>(() => new ProgramManifestResources(null!, program));
        Assert.Throws<ArgumentNullException>(() => new ProgramManifestResources(metadata, null!));
        Assert.Empty(new ProgramManifestResources(metadata, program).KeptResources());
    }
}
