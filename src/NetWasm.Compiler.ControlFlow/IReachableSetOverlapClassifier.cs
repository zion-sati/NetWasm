using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public interface IReachableSetOverlapClassifier
{
    bool Overlaps(ImmutableArray<ImmutableHashSet<int>> reachableSets);
}
