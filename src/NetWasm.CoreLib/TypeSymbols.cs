// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System
{
    using System.Runtime.InteropServices.Sidecars;

    // NetWasm: a program carries no type or assembly names (issue 225). Its type-symbols sidecar supplies them, a type's
    // by runtime type ID and an assembly's as the display text of the entry just past the type IDs by the index the
    // type table gives it, when the program is built with NetWasmTypeNames and its host provides the file; without
    // them a type reads as its ID, type#<id>, as a stack frame without a symbol reads method#<id>, and an assembly
    // name reads AssemblyNameUnavailable.
    internal static class TypeSymbols
    {
        internal const string AssemblyNameUnavailable = "<Assembly name unavailable>";

        // The type-symbols file, its entries in ID order, and each entry's names once something has read them.
        private static SymbolSidecar? s_sidecar;
        private static TypeSymbol?[]? s_symbols;

        internal static void Install(SymbolSidecar? sidecar)
        {
            s_sidecar = sidecar;
            s_symbols = sidecar is null ? null : new TypeSymbol?[sidecar.Count];
        }

        // Whether the program loaded type names, which looking a type up by name needs. Reading the names is what
        // keeps their loader in the program.
        internal static bool Available
        {
            get
            {
                Runtime.InteropServices.ProgramSidecars.LoadTypeSymbols();
                return s_sidecar is not null;
            }
        }

        internal static PlatformNotSupportedException NamesUnavailable() => new(
            "Looking a type up by name needs type names: build with NetWasmTypeNames to load them from the type-symbols sidecar.");

        internal static TypeSymbol? Find(int typeId)
        {
            Runtime.InteropServices.ProgramSidecars.LoadTypeSymbols();
            if (s_sidecar is not { } sidecar || s_symbols is not { } symbols)
            {
                return null;
            }
            var low = 0;
            var high = sidecar.Count - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var id = sidecar.Id(middle);
                if (id == typeId)
                {
                    // The installer checked that every entry has a name and display text.
                    return symbols[middle] ??= new TypeSymbol(sidecar.String(middle, 0)!, sidecar.String(middle, 1),
                        sidecar.String(middle, 2), sidecar.String(middle, 3)!);
                }
                if (id < typeId)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return null;
        }

        internal static string IdName(int typeId) => "type#" + typeId.ToString();
    }

    // One type's names, formed by the compiler as .NET forms them.
    internal sealed class TypeSymbol(string name, string? @namespace, string? fullName, string displayName)
    {
        internal string Name { get; } = name;

        internal string? Namespace { get; } = @namespace;

        internal string? FullName { get; } = fullName;

        internal string DisplayName { get; } = displayName;
    }
}
