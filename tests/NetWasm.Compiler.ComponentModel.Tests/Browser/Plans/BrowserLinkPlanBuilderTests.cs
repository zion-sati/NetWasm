// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkPlanBuilderTests
{
    [Fact]
    public void PreservesSequenceAndOwnershipAndDeduplicatesCapabilitiesBeforeValidation()
    {
        var validator = new RecordingValidator();
        var builder = Create(new TextInspector(), validator);
        ImmutableArray<BrowserLinkOperation> operations = [new BrowserLinkTextOperation("first", "a"), new BrowserLinkTextOperation("second", "b")];

        var result = builder.Build(operations, ["input"], ["output"], ["first", "second"]);

        Assert.Same(result, validator.Plan);
        Assert.Equal(operations, result.Operations);
        Assert.Equal(["text"], result.RequiredCapabilities);
        Assert.Equal(["input"], result.Inputs);
        Assert.Equal(["output"], result.Outputs);
        Assert.Equal(["first", "second"], result.TemporaryPaths);
        Assert.Equal(BrowserLinkPlan.CurrentSchemaVersion, result.SchemaVersion);
    }

    [Fact]
    public void RejectsEmptySequenceBeforeCollaboratorsAndPropagatesValidationFailure()
    {
        var validator = new RecordingValidator { Reject = true };
        var builder = Create(new TextInspector(), validator);
        Assert.Throws<ArgumentException>(() => builder.Build(default, [], [], []));
        Assert.Throws<ArgumentException>(() => builder.Build([], [], [], []));
        Assert.Null(validator.Plan);
        Assert.Throws<InvalidOperationException>(() => builder.Build([new BrowserLinkTextOperation("out", "text")], [], ["out"], []));
        Assert.NotNull(validator.Plan);
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkPlanBuilder(null!, validator));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkPlanBuilder(new TextInspector(), null!));
    }

    private static IBrowserLinkPlanBuilder Create(IBrowserLinkOperationInspector inspector, IBrowserLinkPlanValidator validator) =>
        Assert.IsAssignableFrom<IBrowserLinkPlanBuilder>(new BrowserLinkPlanBuilder(inspector, validator));

    private sealed class TextInspector : IBrowserLinkOperationInspector
    {
        public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation) =>
            new("text", [], [Assert.IsType<BrowserLinkTextOperation>(operation).OutputPath], []);
    }

    private sealed class RecordingValidator : IBrowserLinkPlanValidator
    {
        public BrowserLinkPlan? Plan { get; private set; }
        public bool Reject { get; init; }
        public void Validate(BrowserLinkPlan plan)
        {
            Plan = plan;
            if (Reject) throw new InvalidOperationException("Rejected by validator.");
        }
    }
}
