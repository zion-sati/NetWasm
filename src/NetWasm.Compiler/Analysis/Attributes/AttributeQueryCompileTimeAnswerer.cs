// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: a query whose call names its target and filter is answered from metadata at compile time. The lowering asks
// it of each body as it is read; the reducer asks it again of the settled program, where inlining or a constant argument
// can have named a target a body read on its own did not.
internal sealed class AttributeQueryCompileTimeAnswerer(
    IAttributeQueryResolver resolver,
    IAttributeQueryPlanner planner,
    IAttributeQueryCilBuilder fragments,
    MethodInstanceModel ambiguityConstructor) : IAttributeQueryCompileTimeAnswerer
{
    private readonly IAttributeQueryResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly IAttributeQueryPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly IAttributeQueryCilBuilder _fragments = fragments ?? throw new ArgumentNullException(nameof(fragments));

    public AttributeQueryAnswer Answer(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack, int firstLocal)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (_resolver.Resolve(call, stack) is not { } query)
        {
            return new AttributeQueryAnswer.Unresolved();
        }
        AttributeQueryEntry plan;
        try
        {
            plan = _planner.Plan(query);
        }
        catch (CompilerException exception)
        {
            return new AttributeQueryAnswer.Unbuildable(exception, query.Broad);
        }
        return new AttributeQueryAnswer.Answered(_fragments.Build(new(call.Operation, plan, call.ArgumentCount,
            call.InheritArgument, query.InheritConstant, query.ResultArrayType, ambiguityConstructor, firstLocal)));
    }
}
