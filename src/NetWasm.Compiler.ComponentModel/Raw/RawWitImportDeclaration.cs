using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public abstract record RawWitImportDeclaration(
    string InterfaceName,
    string DeploymentInterfaceName)
{
    protected RawWitImportDeclaration(string interfaceName)
        : this(interfaceName, interfaceName)
    {
    }

    public sealed record Callable(
        string InterfaceName,
        string DeploymentInterfaceName,
        WitFunction Definition) :
        RawWitImportDeclaration(InterfaceName, DeploymentInterfaceName)
    {
        public Callable(string interfaceName, WitFunction definition)
            : this(interfaceName, interfaceName, definition)
        {
        }
    }

    public sealed record Resource(
        string InterfaceName,
        string DeploymentInterfaceName,
        WitTypeDefinition Definition,
        CanonicalAbiFunctionKind Kind) :
        RawWitImportDeclaration(InterfaceName, DeploymentInterfaceName)
    {
        public Resource(
            string interfaceName,
            WitTypeDefinition definition,
            CanonicalAbiFunctionKind kind)
            : this(interfaceName, interfaceName, definition, kind)
        {
        }
    }
}
