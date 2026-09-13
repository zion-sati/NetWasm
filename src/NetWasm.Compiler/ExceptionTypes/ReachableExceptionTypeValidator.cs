using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.ExceptionTypes;

public interface IReachableExceptionTypeValidator
{
    IReadOnlyList<ReachableExceptionType> Validate(
        IEnumerable<ReachableExceptionType> reachableTypes);
}

public sealed class ReachableExceptionTypeValidator : IReachableExceptionTypeValidator
{
    public IReadOnlyList<ReachableExceptionType> Validate(
        IEnumerable<ReachableExceptionType> reachableTypes)
    {
        ArgumentNullException.ThrowIfNull(reachableTypes);
        var entries = reachableTypes
            .OrderBy(entry => entry.TypeId)
            .ThenBy(entry => entry.CanonicalIdentity, StringComparer.Ordinal)
            .ToArray();

        if (entries.Any(entry => entry.TypeId <= 0))
        {
            throw new InvalidOperationException(
                "Reachable exception type IDs must be positive.");
        }
        if (entries.GroupBy(entry => entry.TypeId).Any(group => group.Count() != 1))
        {
            throw new InvalidOperationException(
                "Reachable exception type IDs must be unique.");
        }
        if (entries.GroupBy(
                entry => entry.CanonicalIdentity,
                StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new InvalidOperationException(
                "Reachable exception canonical identities must be unique.");
        }
        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.CanonicalIdentity) ||
                                 string.IsNullOrWhiteSpace(entry.DisplayName) ||
                                 string.IsNullOrWhiteSpace(entry.AssemblyIdentity)))
        {
            throw new InvalidOperationException(
                "Reachable exception identities must be complete.");
        }

        return entries;
    }
}
