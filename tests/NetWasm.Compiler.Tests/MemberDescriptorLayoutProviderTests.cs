// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class MemberDescriptorLayoutProviderTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PublishesCanonicalDescriptorAddressesAndRejectsMissingOnes(WasmTarget target)
    {
        var assembly = new AssemblyIdentity("DescriptorLayouts");
        var typeKey = new EntityKey(assembly, 0x02000001);
        var declaringType = CliTypeIdentity.Named(assembly, "Fixtures", "Target", false);
        var definition = new MethodDefinitionModel(
            new(assembly, 0x06000001), typeKey, "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        var method = new MethodInstanceModel(definition, declaringType, [], definition.Signature);
        var fieldDefinition = new FieldDefinitionModel(
            new(assembly, 0x04000001), typeKey, "Value", CliValueKind.I4, false);
        var field = new FieldInstanceModel(fieldDefinition, declaringType, fieldDefinition.SignatureType);
        var targetLayout = WasmTargetLayout.For(target);
        var data = new ManagedStaticData(256, [], [], [], [], [], [], [], [], [])
        {
            MethodDescriptors = ImmutableDictionary<string, int>.Empty.Add(method.CanonicalName, 64),
            FieldDescriptors = ImmutableDictionary<string, int>.Empty.Add(field.CanonicalName, 80),
            MemberDescriptorDeclaringTypeIdOffset = targetLayout.ObjectHeaderSize,
            MemberDescriptorRequiresDeclaringTypeOffset = targetLayout.ObjectHeaderSize + sizeof(int),
        };
        var provider = new MemberDescriptorLayoutProviderFactory().Create(Snapshot(target, data));

        Assert.Equal(64, provider.GetDescriptorAddress(method));
        Assert.Equal(80, provider.GetDescriptorAddress(field));
        Assert.Equal(targetLayout.ObjectHeaderSize, provider.DeclaringTypeIdOffset);
        Assert.Equal(targetLayout.ObjectHeaderSize + sizeof(int), provider.RequiresDeclaringTypeOffset);

        var missingDefinition = definition with { Key = new(assembly, 0x06000002) };
        var missingMethod = new MethodInstanceModel(
            missingDefinition, declaringType, [], missingDefinition.Signature);
        var missingFieldDefinition = fieldDefinition with { Key = new(assembly, 0x04000002) };
        var missingField = new FieldInstanceModel(
            missingFieldDefinition, declaringType, missingFieldDefinition.SignatureType);
        var methodFailure = Assert.Throws<CompilerException>(() => provider.GetDescriptorAddress(missingMethod));
        var fieldFailure = Assert.Throws<CompilerException>(() => provider.GetDescriptorAddress(missingField));
        Assert.Equal(DiagnosticCode.RuntimeContract, methodFailure.Diagnostic.Code);
        Assert.Equal(DiagnosticCode.RuntimeContract, fieldFailure.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMissingSnapshot()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new MemberDescriptorLayoutProvider(null!));

        Assert.Equal("snapshot", error.ParamName);
    }

    internal static ManagedLayoutSnapshot Snapshot(WasmTarget target, ManagedStaticData data) => new(
        new ManagedTypeLayouts([], [], [], [], new ManagedValueLayoutState(),
            WasmTargetLayout.For(target), 0, 0, 0, 0), data);
}
