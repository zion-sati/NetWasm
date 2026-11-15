// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkExecutionPlanBuilderTests
{
    [Fact]
    public void PreparesToolsAndValidationWithoutReorderingOrRewritingLogicalArtifacts()
    {
        var plan = new BrowserLinkPlan(1, ["tool", "validate"], ["v/input"], ["v/output"], [],
            [new BrowserLinkToolOperation("wasm-opt", ["v/input", "-Oz", "--output", "v/output"], ["v/input"], ["v/output"]),
                new BrowserLinkValidationOperation("v/output", ["validate", "v/output", "--features", "mvp"])]);

        var result = BrowserLinkPlanValidation.CreateExecutionBuilder().Build(plan);

        Assert.Same(plan, result.Plan);
        Assert.Equal(2, result.Invocations.Count);
        Assert.Equal(["input-0", "-Oz", "--output", "output-0"], result.Invocations[0].Arguments);
        Assert.Equal(["validate", "input-0", "--features", "mvp"], result.Invocations[1].Arguments);
        Assert.Equal("wasm-tools", result.Invocations[1].ToolId);
        Assert.Equal("v/output", Assert.Single(result.Invocations[1].Inputs).LogicalPath);
        Assert.Empty(result.Invocations[1].Outputs);
        using var json = JsonDocument.Parse(new BrowserLinkPlanSerializer().Serialize(result));
        Assert.Equal("tool", json.RootElement.GetProperty("plan").GetProperty("operations")[0].GetProperty("kind").GetString());
        Assert.Equal("wasm-tools", json.RootElement.GetProperty("invocations").GetProperty("1").GetProperty("toolId").GetString());
    }

    [Fact]
    public void OmitsInvocationsForNonToolOperations()
    {
        var plan = new BrowserLinkPlan(1, ["text", "validate"], [], ["output"], [],
            [new BrowserLinkTextOperation("output", "(module)"),
                new BrowserLinkValidationOperation("output", ["validate", "output", "--features", "mvp"])]);
        var result = BrowserLinkPlanValidation.CreateExecutionBuilder().Build(plan);
        Assert.Equal(1, Assert.Single(result.Invocations).Key);
    }

    [Fact]
    public void MissingDependenciesAndInvalidPlansFailBeforeToolMaterialization()
    {
        var validator = new RejectingValidator();
        var inspector = new BrowserLinkTextInspector();
        var materializer = new ForbiddenMaterializer();
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkExecutionPlanBuilder(null!, inspector, materializer));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkExecutionPlanBuilder(validator, null!, materializer));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkExecutionPlanBuilder(validator, inspector, null!));
        var builder = Assert.IsAssignableFrom<IBrowserLinkExecutionPlanBuilder>(new BrowserLinkExecutionPlanBuilder(validator, inspector, materializer));
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        var plan = new BrowserLinkPlan(99, [], [], [], [], []);
        Assert.Throws<ArgumentException>(() => builder.Build(plan));
        Assert.Same(plan, validator.Received);
        Assert.False(materializer.Called);
    }

    private sealed class RejectingValidator : IBrowserLinkPlanValidator
    {
        public BrowserLinkPlan? Received { get; private set; }
        public void Validate(BrowserLinkPlan plan)
        {
            Received = plan;
            throw new ArgumentException("Unsupported plan.");
        }
    }

    private sealed class ForbiddenMaterializer : IBrowserLinkToolMaterializer
    {
        public bool Called { get; private set; }
        public BrowserLinkToolInvocation Materialize(BrowserLinkOperation operation)
        {
            Called = true;
            throw new InvalidOperationException("Must not materialize invalid plans.");
        }
    }
}
