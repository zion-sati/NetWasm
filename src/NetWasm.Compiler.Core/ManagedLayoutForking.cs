using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

/// <summary>Worker-local capabilities for value and instance-field layouts.</summary>
public sealed class ManagedLayoutFork(
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider fields)
{
    public IValueLayoutProvider Values { get; } = values ??
        throw new System.ArgumentNullException(nameof(values));
    public IInstanceFieldLayoutProvider Fields { get; } = fields ??
        throw new System.ArgumentNullException(nameof(fields));
}

/// <summary>A request-owned set of layout forks and its publication capability.</summary>
public sealed class ManagedLayoutForks(
    ImmutableArray<ManagedLayoutFork> workers,
    IManagedLayoutForkPublisher publisher)
{
    public ImmutableArray<ManagedLayoutFork> Workers { get; } = workers;
    public IManagedLayoutForkPublisher Publisher { get; } = publisher ??
        throw new System.ArgumentNullException(nameof(publisher));
}

public interface IManagedLayoutForkPublisher
{
    void Publish();
}

public interface IManagedLayoutForkSource
{
    ManagedLayoutForks Create(int workerCount);
}

public interface IManagedLayoutForkSourceFactory
{
    IManagedLayoutForkSource Create(
        ITargetLayout layout,
        ITypeRepository types,
        ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fields);
}
