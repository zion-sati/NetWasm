// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Optimization;

public sealed class ManagedInliningPackagingTests
{
    [Theory]
    [InlineData(ManagedInliningHints.IgnoreAggressive)]
    [InlineData(ManagedInliningHints.Preserve)]
    public void RejectsOverridesWithoutALinkStageBeforeAccessingInputs(ManagedInliningHints hint)
    {
        var inputs = new RecordingExistence();
        var validator = Assert.IsAssignableFrom<IComponentPackageInputValidator>(new ComponentPackageInputValidator(inputs, inputs));
        var request = new ComponentPackageRequest("core", "wit", null, "output", ComponentTarget.Wasm32Wasi02)
        { OptimizationOverrides = new(hint) };
        var error = Assert.Throws<CompilerException>(() => validator.Validate(request));
        Assert.Contains("require core-module linking", error.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(0, inputs.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticHintsDoNotRequireAnAdditionalLinkStage(bool explicitAuto)
    {
        var inputs = new RecordingExistence();
        var validator = Assert.IsAssignableFrom<IComponentPackageInputValidator>(new ComponentPackageInputValidator(inputs, inputs));
        validator.Validate(new("core", "wit", null, "output", ComponentTarget.Wasm32Wasi02)
        { OptimizationOverrides = explicitAuto ? new() : null });
        Assert.Equal(2, inputs.Calls);
    }

    [Fact]
    public void ExplicitHintsAreAcceptedWhenLinkingIsPresent()
    {
        var inputs = new RecordingExistence();
        var validator = Assert.IsAssignableFrom<IComponentPackageInputValidator>(new ComponentPackageInputValidator(inputs, inputs));
        validator.Validate(new("core", "wit", null, "output", ComponentTarget.Wasm32Wasi02, "runtime")
        { OptimizationOverrides = new(ManagedInliningHints.Preserve) });
        Assert.Equal(2, inputs.Calls);
    }

    private sealed class RecordingExistence : IFileExistence, IDirectoryExistence
    {
        public int Calls { get; private set; }

        public bool Exists(string path)
        {
            Calls++;
            return true;
        }
    }
}
