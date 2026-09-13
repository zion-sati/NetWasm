namespace NetWasm.Sdk.Pack.Provenance;

public interface ISdkProvenanceManifestBuilder
{
    byte[] Build(SdkProvenanceManifestInput input);
}
