// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Inlining;

public sealed class ManagedInliningBodyValidatorTests
{
    [Fact]
    public void ReturnsTypedInstructionEntryStacksForTheSuppliedBody()
    {
        var method = ValidationTestData.ManagedMethodBody();
        var graphBuilder = new RecordingGraphBuilder(
            method.Body,
            method.ControlFlow.Graph);
        var stackValidator = new RecordingStackValidator(
            method.ControlFlow.Graph,
            method.ControlFlow);

        var result = new ManagedInliningBodyValidator(
            graphBuilder,
            stackValidator).Validate(method.Body);

        Assert.Same(method.ControlFlow.InstructionEntryStacks, result);
        Assert.Equal(1, graphBuilder.Calls);
        Assert.Equal(1, stackValidator.Calls);
    }

    [Fact]
    public void RejectsMissingDependenciesAndBody()
    {
        var method = ValidationTestData.ManagedMethodBody();
        var graphBuilder = new RecordingGraphBuilder(
            method.Body,
            method.ControlFlow.Graph);
        var stackValidator = new RecordingStackValidator(
            method.ControlFlow.Graph,
            method.ControlFlow);

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedInliningBodyValidator(null!, stackValidator));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedInliningBodyValidator(graphBuilder, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedInliningBodyValidator(graphBuilder, stackValidator)
                .Validate(null!));
    }

    private sealed class RecordingGraphBuilder(
        CilMethodBody expected,
        ControlFlowGraph result) : IControlFlowGraphBuilder
    {
        public int Calls { get; private set; }

        public ControlFlowGraph Build(CilMethodBody methodBody)
        {
            Assert.Same(expected, methodBody);
            Calls++;
            return result;
        }
    }

    private sealed class RecordingStackValidator(
        ControlFlowGraph expected,
        ValidatedControlFlowGraph result) : ITypedStackValidator
    {
        public int Calls { get; private set; }

        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph)
        {
            Assert.Same(expected, graph);
            Calls++;
            return result;
        }
    }
}
