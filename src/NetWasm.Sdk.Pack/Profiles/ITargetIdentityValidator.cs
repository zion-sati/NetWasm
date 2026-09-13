using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Profiles;

public interface ITargetIdentityValidator
{
    ImmutableArray<TargetOutputIdentity> Validate(IReadOnlyList<TargetOutputIdentity> identities);
}
