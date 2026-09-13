using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IModuleInitializerOrderer
{
    ImmutableArray<EntityKey> Order(IEnumerable<EntityKey> initializers);
}

/// <summary>Orders retained module initializers after their assembly dependencies.</summary>
internal sealed class ModuleInitializerOrderer(
    IReadOnlyDictionary<AssemblyIdentity, IReadOnlyList<AssemblyIdentity>> dependencies) :
    IModuleInitializerOrderer
{
    private readonly IReadOnlyDictionary<AssemblyIdentity, IReadOnlyList<AssemblyIdentity>>
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public ImmutableArray<EntityKey> Order(IEnumerable<EntityKey> initializers)
    {
        ArgumentNullException.ThrowIfNull(initializers);
        var byAssembly = initializers.ToDictionary(initializer => initializer.Assembly);
        var states = new Dictionary<AssemblyIdentity, VisitState>();
        var ordered = ImmutableArray.CreateBuilder<EntityKey>(byAssembly.Count);
        foreach (var assembly in byAssembly.Keys.OrderBy(identity => identity.Name, StringComparer.Ordinal))
        {
            Visit(assembly);
        }

        return ordered.MoveToImmutable();

        void Visit(AssemblyIdentity assembly)
        {
            if (states.TryGetValue(assembly, out var state))
            {
                if (state == VisitState.Visiting)
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.UnsupportedMetadata,
                        $"module initializer dependency cycle includes assembly '{assembly.Name}'"));
                }

                return;
            }

            states.Add(assembly, VisitState.Visiting);
            if (_dependencies.TryGetValue(assembly, out var dependencies))
            {
                foreach (var dependency in dependencies
                             .Where(byAssembly.ContainsKey)
                             .OrderBy(identity => identity.Name, StringComparer.Ordinal))
                {
                    Visit(dependency);
                }
            }

            states[assembly] = VisitState.Visited;
            ordered.Add(byAssembly[assembly]);
        }
    }

    private enum VisitState
    {
        Visiting,
        Visited,
    }
}
