namespace NetWasm.Hosting.Deployment;

/// <summary>Emits the deterministic ESM boundary for one generated component.</summary>
public interface ICanonicalComponentAdapterWriter
{
    byte[] Write(CanonicalComponentAdapterRequest request);
}
