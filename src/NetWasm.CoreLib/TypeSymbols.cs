namespace System
{
    using System.Collections.Generic;

    // NetWasm: a program carries no type names (issue 225). Its type-symbols sidecar supplies them by runtime type ID
    // when the program is built with NetWasmTypeNames and its host provides the file; without them a type reads as
    // its ID, type#<id>, as a stack frame without a symbol reads method#<id>. There are no assemblies at run time, so
    // an assembly name always reads AssemblyNameUnavailable.
    internal static class TypeSymbols
    {
        internal const string AssemblyNameUnavailable = "<Assembly name unavailable>";

        private static Dictionary<int, TypeSymbol>? s_symbols;

        internal static void Install(Dictionary<int, TypeSymbol> symbols) => s_symbols = symbols;

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
