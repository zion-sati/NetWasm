namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public interface INetWasmHostComponentShimWriter
{
    void Write(NetWasmHostComponentShimRequest request);
}
