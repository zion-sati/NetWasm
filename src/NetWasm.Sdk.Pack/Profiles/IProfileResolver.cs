namespace NetWasm.Sdk.Pack.Profiles;

public interface IProfileResolver
{
    TargetProfile Resolve(string profileAlias);
}
