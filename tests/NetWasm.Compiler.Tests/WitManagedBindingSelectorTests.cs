// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class WitManagedBindingSelectorTests
{
    [Fact]
    public void SelectPrefersBindingsOwnedByTheEntryAssembly()
    {
        var entry = new AssemblyIdentity("entry");
        var owned = CreateMethod(entry, 1);
        var referenced = CreateMethod(new AssemblyIdentity("referenced"), 2);

        Action<IWitManagedBindingSelector> contract = selector =>
        {
            var selected = selector.Select(
                CreateMetadata(entry),
                [referenced, owned]);

            var result = Assert.Single(selected);
            Assert.Same(owned, result);
        };
        contract(CreateSelector());
    }

    [Fact]
    public void SelectUsesAllCandidatesWhenEntryAssemblyHasNoBinding()
    {
        var entry = new AssemblyIdentity("entry");
        var first = CreateMethod(new AssemblyIdentity("first"), 1);
        var second = CreateMethod(new AssemblyIdentity("second"), 2);

        Action<IWitManagedBindingSelector> contract = selector =>
        {
            var selected = selector.Select(
                CreateMetadata(entry),
                [first, second]);

            Assert.Equal(2, selected.Length);
            Assert.Same(first, selected[0]);
            Assert.Same(second, selected[1]);
        };
        contract(CreateSelector());
    }

    [Fact]
    public void SelectRejectsMissingInputs()
    {
        var metadata = CreateMetadata(new AssemblyIdentity("entry"));

        Action<IWitManagedBindingSelector> contract = selector =>
        {
            Assert.Throws<ArgumentNullException>(() => selector.Select(null!, []));
            Assert.Throws<ArgumentNullException>(() => selector.Select(metadata, null!));
        };
        contract(CreateSelector());
    }

    private static WitManagedBindingSelector CreateSelector() =>
        new WitManagedBindingSelector();

    private static MetadataCompilationSnapshot CreateMetadata(AssemblyIdentity entry) =>
        new([], entry, [], [], []);

    private static MethodDefinitionModel CreateMethod(AssemblyIdentity assembly, int token) =>
        new(
            new EntityKey(assembly, token),
            new EntityKey(assembly, 0x02000001),
            $"Method{token}",
            IsStatic: true,
            MethodSignatureModel.Create(CliValueKind.Void),
            RelativeVirtualAddress: 0);

}
