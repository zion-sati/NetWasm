using System;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentPackagingCapability
{
    void EnsureSupported(ComponentTarget target);
}

public sealed class ComponentPackagingCapability : IComponentPackagingCapability
{
    public void EnsureSupported(ComponentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.WasiVersion != "0.2")
        {
            throw ComponentException.Invalid(
                "component packaging supports only WASI 0.2");
        }
        if (target.Width == "wasm64")
        {
            throw ComponentException.Tool(
                "wasm64 component packaging is unavailable because the current " +
                "stable component encoder supports only the cm32p2 producer ABI; " +
                "use wasm32 for component output or emit a wasm64 core module until " +
                "a compatible memory64 component toolchain is available");
        }
        if (target.Width != "wasm32")
        {
            throw ComponentException.Invalid(
                $"unsupported component target width '{target.Width}'");
        }
    }
}
