namespace System
{
    using System.Collections.Generic;

    // NetWasm: a program carries no type or assembly names (issue 225). Its type-symbols sidecar supplies them, a type's
    // by runtime type ID and an assembly's as the display text of the entry just past the type IDs by the index the
    // type table gives it, when the program is built with NetWasmTypeNames and its host provides the file; without
    // them a type reads as its ID, type#<id>, as a stack frame without a symbol reads method#<id>, and an assembly
    // name reads AssemblyNameUnavailable.
    internal static class TypeSymbols
    {
        internal const string AssemblyNameUnavailable = "<Assembly name unavailable>";

        private static Dictionary<int, TypeSymbol>? s_symbols;

        internal static void Install(Dictionary<int, TypeSymbol> symbols) => s_symbols = symbols;

        // Whether the program loaded type names, which looking a type up by name needs.
        internal static bool Available => s_symbols is not null;

        internal static PlatformNotSupportedException NamesUnavailable() => new(
            "Looking a type up by name needs type names: build with NetWasmTypeNames to load them from the type-symbols sidecar.");

        internal static TypeSymbol? Find(int typeId) =>
            s_symbols is { } symbols && symbols.TryGetValue(typeId, out var symbol) ? symbol : null;

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
