namespace NetWasm.Toolchain.Prerequisites;

public interface IHostToolCompatibilityProbe
{
    HostToolCompatibilityObservation Observe(ResolvedHostExecutable executable);
}
