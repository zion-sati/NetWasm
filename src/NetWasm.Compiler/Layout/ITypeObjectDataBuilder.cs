namespace NetWasm.Compiler.Layout;

internal interface ITypeObjectDataBuilder
{
    void Build();
}

internal sealed class EmptyTypeObjectDataBuilder : ITypeObjectDataBuilder
{
    public static EmptyTypeObjectDataBuilder Instance { get; } = new();

    public void Build()
    {
    }
}
