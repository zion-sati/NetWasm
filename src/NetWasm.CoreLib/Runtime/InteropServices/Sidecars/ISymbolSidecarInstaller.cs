namespace System.Runtime.InteropServices.Sidecars
{
    // Gives the program what a parsed symbols sidecar holds; throws FormatException, installing nothing, for
    // contents it cannot use.
    internal interface ISymbolSidecarInstaller
    {
        void Install(SymbolSidecar sidecar);
    }

    internal interface IRuntimeMethodSymbolRegistrar
    {
        void Register(int id, ReadOnlySpan<char> text);
    }

    internal interface ISymbolSidecarLoader
    {
        void Load(SidecarDescriptor descriptor);
    }
}
