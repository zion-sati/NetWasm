// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkPlanValidatorTests
{
    [Fact]
    public void AcceptsProducerOrderedArtifactsAndValidatedPublication() =>
        BrowserLinkPlanValidation.Create().Validate(Plan());

    [Fact]
    public void AcceptsAnnotationCleanupAfterValidationAndInPlaceEdits()
    {
        var plan = Plan();
        plan = plan with
        {
            Operations = plan.Operations.Insert(4,
                new BrowserLinkInlineOperation("merged", "merged", WasmInlineAnnotationSelection.Remove)),
        };

        BrowserLinkPlanValidation.Create().Validate(plan);
    }

    [Fact]
    public void AllowsTemporaryReuseAfterItsPreviousValueWasRead()
    {
        var plan = Plan();
        plan = plan with
        {
            RequiredCapabilities = plan.RequiredCapabilities.Add("exports"),
            Operations = plan.Operations.SetItem(3,
                    new BrowserLinkExportOperation("merged", "selected", null, []))
                .Insert(4, Validation("selected"))
                .SetItem(5, new BrowserLinkMoveOperation("selected", "result")),
        };

        BrowserLinkPlanValidation.Create().Validate(plan);
    }

    [Fact]
    public void RejectsUninitializedVersionsOwnershipAndCapabilities()
    {
        var plan = Plan();
        foreach (var invalid in new[]
        {
            plan with { SchemaVersion = 2 },
            plan with { Operations = default },
            plan with { Operations = [] },
            plan with { Inputs = default },
            plan with { Outputs = default },
            plan with { Outputs = [] },
            plan with { TemporaryPaths = default },
            plan with { Inputs = ["app", "app"] },
            plan with { Outputs = ["app"] },
            plan with { TemporaryPaths = plan.TemporaryPaths.Add("app") },
            plan with { TemporaryPaths = plan.TemporaryPaths.Add("result") },
            plan with { TemporaryPaths = plan.TemporaryPaths.Add("app/child") },
            plan with { TemporaryPaths = plan.TemporaryPaths.Add("/absolute") },
            plan with { RequiredCapabilities = default },
            plan with { RequiredCapabilities = [] },
            plan with { RequiredCapabilities = plan.RequiredCapabilities.Add("text") },
            plan with { RequiredCapabilities = plan.RequiredCapabilities.Add(" ") },
            plan with { RequiredCapabilities = plan.RequiredCapabilities.Add("unknown") },
        }) Assert.Throws<ArgumentException>(() => BrowserLinkPlanValidation.Create().Validate(invalid));
    }

    [Fact]
    public void RejectsUnavailableInputsUnownedWritesAndUnvalidatedFinalArtifacts()
    {
        var plan = Plan();
        foreach (var operations in new[]
        {
            plan.Operations.SetItem(1, new BrowserLinkInlineOperation("missing", "selected", WasmInlineAnnotationSelection.Preserve)),
            plan.Operations.SetItem(1, new BrowserLinkInlineOperation("app", "runtime", WasmInlineAnnotationSelection.Preserve)),
            plan.Operations.SetItem(1, new BrowserLinkInlineOperation("app", "undeclared", WasmInlineAnnotationSelection.Preserve)),
            plan.Operations.Insert(1, new BrowserLinkTextOperation("support", "replacement")),
            plan.Operations.SetItem(4, new BrowserLinkMoveOperation("app", "result")),
            plan.Operations.Insert(5, new BrowserLinkInlineOperation("merged", "selected", WasmInlineAnnotationSelection.Remove)),
            plan.Operations.Insert(4, new BrowserLinkTextOperation("merged", "unvalidated replacement")),
            plan.Operations.SetItem(4, new BrowserLinkMoveOperation("merged", "selected")),
        }) Assert.Throws<ArgumentException>(() => BrowserLinkPlanValidation.Create().Validate(plan with { Operations = operations }));
    }

    [Fact]
    public void RejectsMissingDependenciesAndNullPlans()
    {
        var paths = new BrowserLinkPathValidator();
        var operations = new BrowserLinkTextInspector();
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkPlanValidator(null!, operations));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkPlanValidator(paths, null!));
        Assert.Throws<ArgumentNullException>(() => BrowserLinkPlanValidation.Create().Validate(null!));
    }

    private static BrowserLinkValidationOperation Validation(string path) =>
        new(path, ["validate", path, "--features", "mvp,reference-types"]);

    private static BrowserLinkPlan Plan() => new(1,
        ["text", "inline", "tool", "validate", "move"], ["app", "runtime"], ["result"],
        ["support", "selected", "merged"],
        [
            new BrowserLinkTextOperation("support", "support module"),
            new BrowserLinkInlineOperation("app", "selected", WasmInlineAnnotationSelection.SizeAware),
            new BrowserLinkToolOperation("wasm-merge",
                ["selected", "application", "runtime", "runtime", "support", "env", "--output", "merged", "--enable-reference-types"],
                ["selected", "runtime", "support"], ["merged"]),
            Validation("merged"),
            new BrowserLinkMoveOperation("merged", "result"),
        ]);
}
