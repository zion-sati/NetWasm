using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedLayoutForkSetFactory(
    ManagedLayoutSnapshot snapshot,
    ITypeRepository types,
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    IValueLayoutResolverFactory resolvers,
    IValueLayoutProviderFactory values,
    IInstanceFieldLayoutProviderFactory fieldProviders) :
    IManagedLayoutForkSource
{
    public ManagedLayoutForks Create(int workerCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);

        var original = snapshot.TypeLayouts.ValueLayoutState;
        var baseline = new ManagedValueLayoutState(original);
        var workerStates = new ManagedValueLayoutState[workerCount];
        var forks = ImmutableArray.CreateBuilder<ManagedLayoutFork>(workerCount);
        for (var index = 0; index < workerCount; index++)
        {
            var state = new ManagedValueLayoutState(baseline);
            var workerSnapshot = new ManagedLayoutSnapshot(
                snapshot.TypeLayouts with { ValueLayoutState = state },
                snapshot.StaticData);
            var resolver = resolvers.Create(
                typeDefinitions, fields, snapshot.Target, state);
            var valueProvider = values.Create(resolver);
            var fieldProvider = new ResolvingInstanceFieldLayoutProvider(
                fieldProviders.Create(workerSnapshot), types, fields, resolver);
            workerStates[index] = state;
            forks.Add(new(valueProvider, fieldProvider));
        }

        return new(forks.MoveToImmutable(),
            new ManagedLayoutForkPublisher(original, workerStates));
    }
}
