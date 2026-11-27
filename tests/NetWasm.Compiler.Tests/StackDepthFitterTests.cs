// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A rewritten body's maximum stack rises to the depth its stack reaches and never falls; a body within it is returned
// as it is.
public sealed class StackDepthFitterTests
{
    // ldc.i4 1; dup; add; pop; ret reaches depth 2.
    private static readonly ImmutableArray<CilInstruction> Code =
    [
        new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new(1, 2, CilOperation.Duplicate, new CilOperand.None()),
        new(2, 3, CilOperation.Add, new CilOperand.None()),
        new(3, 4, CilOperation.Pop, new CilOperand.None()),
        new(4, 5, CilOperation.Return, new CilOperand.None()),
    ];

    [Fact]
    public void RaisesTheMaximumToTheDepthReachedAndNeverLowersIt()
    {
        var fitter = Fitter();

        Assert.Equal(2, fitter.Fit(Body(1)).MaxStack);
        var within = Body(2);
        Assert.Same(within, fitter.Fit(within));
        var above = Body(5);
        Assert.Same(above, fitter.Fit(above));
    }

    [Fact]
    public void RejectsMissingCollaboratorsAndBodies()
    {
        var program = new Program();
        var validator = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
        Assert.Throws<ArgumentNullException>(() => new StackDepthFitter(null!, validator));
        Assert.Throws<ArgumentNullException>(() => new StackDepthFitter(new ControlFlowGraphBuilderFactory().Create(), null!));
        Assert.Throws<ArgumentNullException>(() => Fitter().Fit(null!));
    }

    private static StackDepthFitter Fitter()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program));
    }

    private static CilMethodBody Body(int maxStack) =>
        new(new MethodDefinitionModel(new(new AssemblyIdentity("fixture"), 0x06000001),
            new(new AssemblyIdentity("fixture"), 0x02000002), "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1),
            maxStack, [], Code);

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
