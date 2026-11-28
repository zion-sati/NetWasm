// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// A newobj may publish only on its allocation's fallback where allocations bump inline (ConditionalAllocationRoots) and
// the constructor call is no safepoint of its own, in a method with protected blocks too: the emitter decides per
// instruction, and listing a newobj it publishes in place only means that newobj does not count as entering the frame.
public sealed class FallbackPublicationClassifierTests
{
    private static readonly RootSource Root = new(RootSourceKind.Local, 0);
    private readonly IFallbackPublicationClassifier _classifier =
        Assert.IsAssignableFrom<IFallbackPublicationClassifier>(new FallbackPublicationClassifier());

    [Fact]
    public void ANewobjWhereAllocationsBumpInlinePublishesOnItsFallback()
    {
        Assert.Equal([0], _classifier.Classify(Method(), Context(constructorCall: false, conditional: true)).Order());
    }

    // A method with protected blocks lists its newobjs as well: the emitter may still defer one outside them, so
    // none of them may count as entering the frame on every path.
    [Fact]
    public void AMethodWithProtectedBlocksListsItsNewobjsToo()
    {
        var protectedMethod = Method() with
        {
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty.Add(
                new(0), new(new(0), null, 0, 1, [], [], [], null, null)),
        };

        Assert.Equal([0], _classifier.Classify(protectedMethod, Context(constructorCall: false, conditional: true)).Order());
    }

    [Fact]
    public void ANewobjOtherwisePublishesWhereItStands()
    {
        Assert.Empty(_classifier.Classify(Method(), Context(constructorCall: false, conditional: false)));
        Assert.Empty(_classifier.Classify(Method(), Context(constructorCall: true, conditional: true)));
        Assert.Throws<ArgumentNullException>(() => _classifier.Classify(null!, CreateMethodEmissionContext()));
        Assert.Throws<ArgumentNullException>(() => _classifier.Classify(Method(), null!));
    }

    // One block: a newobj at 0 and a call at 1, then a return.
    private static StructuredMethod Method()
    {
        var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
            .Add(new(0), new(new(0), 0, [I(0, CilOperation.NewObject), I(1, CilOperation.Call)], [],
                new StructuredTerminalExit(I(2, CilOperation.Return)))
            { EndOffset = 2 });
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1);
        return new(new(definition, null, 1, [], [], []), new(0), blocks, StructuredSequence.Empty, [], [], []);
    }

    // A live root at the newobj and at the call; the newobj's constructor call a safepoint of its own or not.
    private static MethodEmissionContext Context(bool constructorCall, bool conditional) => CreateMethodEmissionContext() with
    {
        ConditionalAllocationRoots = conditional,
        RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(Root, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty
                .Add(0, new(0, [Root], constructorCall ? [Root] : []))
                .Add(1, new(1, [Root], []))),
    };
}
