namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeModuleMaterializer
{
    RuntimeMaterialization Materialize(RuntimeMaterializationRequest request);
}
