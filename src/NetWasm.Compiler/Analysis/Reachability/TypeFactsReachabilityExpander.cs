using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis.Reachability;

internal sealed class TypeFactsReachabilityExpander(
    IRuntimeTypeFactsClosurePlanner typeFactsClosure,
    ITypeIdentityResolver typeIdentities) : ITypeFactsReachabilityExpander
{
    private readonly IRuntimeTypeFactsClosurePlanner _typeFactsClosure = typeFactsClosure ??
        throw new ArgumentNullException(nameof(typeFactsClosure));
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ??
        throw new ArgumentNullException(nameof(typeIdentities));

    public void Expand(ReachabilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State.RequiresGenericArguments || session.State.RequiresDeclaringTypes || session.State.RequiresGenericDefinitions ||
            session.State.RequiresElementTypes || session.State.RequiresTypeAttributes || session.State.RequiresInterfaces)
        {
            session.State.RequiresTypeFacts = true;
        }
        if (session.State.RequiresTypeFacts)
        {
            AddTypeFactsClosure(session);
        }
    }

    private void AddTypeFactsClosure(ReachabilitySession session)
    {
        var required = _typeFactsClosure.Plan(
            [.. session.State.Types.Select(_typeIdentities.GetTypeIdentity), .. session.State.ConstructedTypes],
            session.State.RequiresGenericArguments,
            session.State.RequiresDeclaringTypes,
            session.State.RequiresGenericDefinitions,
            session.State.RequiresElementTypes);
        foreach (var type in required)
        {
            session.AddRuntimeType(type);
            session.AddConstructedType(type, retainElementModifierIdentity: session.State.RequiresGenericArguments);
        }
    }
}
