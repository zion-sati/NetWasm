namespace System.Runtime.InteropServices.Sidecars
{
    using System.Collections.Generic;

    // Gives Type's name readers the type-symbols file's names: name, namespace, full name, display text.
    internal sealed class TypeSymbolInstaller : ISymbolSidecarInstaller
    {
        public void Install(SymbolSidecar sidecar)
        {
            if (sidecar == null)
            {
                throw new ArgumentNullException();
            }
            var symbols = new Dictionary<int, TypeSymbol>(sidecar.Count);
            for (var entry = 0; entry < sidecar.Count; entry++)
            {
                var name = sidecar.String(entry, 0);
                var display = sidecar.String(entry, 3);
                if (name == null || display == null)
                {
                    throw new FormatException("a type has no name");
                }
                symbols.Add(sidecar.Id(entry), new TypeSymbol(
                    name, sidecar.String(entry, 1), sidecar.String(entry, 2), display));
            }
            TypeSymbols.Install(symbols);
        }
    }
}
