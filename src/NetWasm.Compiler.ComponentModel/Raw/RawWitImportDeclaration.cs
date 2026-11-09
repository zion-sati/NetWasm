using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public abstract record RawWitImportDeclaration(
    string InterfaceName,
    string DeploymentInterfaceName)
{
    public string CoreInterfaceName { get; init; } = InterfaceName;

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

        /// <summary>An async func lowered with <c>async</c>: it returns a subtask status.</summary>
        public bool Asynchronous { get; init; }
    }

    /// <summary>
    /// One Component Model built-in for a stream or future type in a function's signature:
    /// the type is the function's <see cref="Index"/>th, counted as the bindings number them.
    /// </summary>
    public sealed record Payload(
        string InterfaceName,
        string DeploymentInterfaceName,
        WitFunction Function,
        int Index,
        int TypeId,
        RawPayloadOperation Operation) :
        RawWitImportDeclaration(InterfaceName, DeploymentInterfaceName);

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

/// <summary>The stream and future built-ins a raw module imports for a payload type.</summary>
public enum RawPayloadOperation
{
    StreamNew,
    StreamRead,
    StreamWrite,
    StreamDropReadable,
    StreamDropWritable,
    FutureNew,
    FutureRead,
    FutureWrite,
    FutureDropReadable,
    FutureDropWritable,
}
