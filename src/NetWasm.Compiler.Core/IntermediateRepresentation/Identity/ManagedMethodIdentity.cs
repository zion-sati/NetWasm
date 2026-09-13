using System;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

public readonly record struct ManagedMethodIdentity
{
    public ManagedMethodIdentity(string canonicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        CanonicalName = canonicalName;
    }

    public string CanonicalName { get; }

    public override string ToString() => CanonicalName;
}
