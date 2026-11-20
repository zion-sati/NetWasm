// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Sidecars
{
    // Gives Type's name readers the type-symbols file's names: name, namespace, full name, display text. Every type
    // must have a name and display text; the names are made as readers ask for them.
    internal sealed class TypeSymbolInstaller : ISymbolSidecarInstaller
    {
        public void Install(SymbolSidecar sidecar)
        {
            if (sidecar == null)
            {
                throw new ArgumentNullException();
            }
            for (var entry = 0; entry < sidecar.Count; entry++)
            {
                sidecar.Text(entry, 0, out var named);
                sidecar.Text(entry, 3, out var displayed);
                if (!named || !displayed)
                {
                    throw new FormatException("a type has no name");
                }
            }
            TypeSymbols.Install(sidecar);
        }
    }
}
