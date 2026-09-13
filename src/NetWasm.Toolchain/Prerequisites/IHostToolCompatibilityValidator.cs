namespace NetWasm.Toolchain.Prerequisites;

public interface IHostToolCompatibilityValidator
{
    ValidatedHostToolCompatibility Validate(HostToolCompatibilityObservation observation);
}
