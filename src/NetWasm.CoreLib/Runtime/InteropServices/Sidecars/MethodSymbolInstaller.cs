namespace System.Runtime.InteropServices.Sidecars
{
    // Gives the runtime each frame's text from the method-symbols file, for the stack traces it builds.
    internal sealed class MethodSymbolInstaller(IRuntimeMethodSymbolRegistrar runtime) : ISymbolSidecarInstaller
    {
        private readonly IRuntimeMethodSymbolRegistrar _runtime =
            runtime ?? throw new ArgumentNullException();

        public void Install(SymbolSidecar sidecar)
        {
            if (sidecar == null)
            {
                throw new ArgumentNullException();
            }
            // Checked whole first, so a bad file registers nothing.
            for (var entry = 0; entry < sidecar.Count; entry++)
            {
                if (sidecar.Id(entry) == 0 || sidecar.Text(entry, 0, out var present).IsEmpty || !present)
                {
                    throw new FormatException("a frame has no text");
                }
            }
            for (var entry = 0; entry < sidecar.Count; entry++)
            {
                _runtime.Register(sidecar.Id(entry), sidecar.Text(entry, 0, out _));
            }
        }
    }

    // The runtime copies the text into its own symbol table.
    internal sealed class RuntimeMethodSymbolRegistrar : IRuntimeMethodSymbolRegistrar
    {
        public unsafe void Register(int id, ReadOnlySpan<char> text)
        {
            fixed (char* characters = text)
            {
                ProgramSidecars.RegisterMethodSymbol(id, characters, text.Length);
            }
        }
    }
}
