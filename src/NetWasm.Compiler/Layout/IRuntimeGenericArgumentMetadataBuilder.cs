namespace NetWasm.Compiler.Layout;

internal interface IRuntimeGenericArgumentMetadataBuilder
{
    void Build();
}

internal sealed class EmptyRuntimeGenericArgumentMetadataBuilder :
    IRuntimeGenericArgumentMetadataBuilder
{
    internal static EmptyRuntimeGenericArgumentMetadataBuilder Instance { get; } = new();

    private EmptyRuntimeGenericArgumentMetadataBuilder()
    {
    }

    public void Build()
    {
    }
}
