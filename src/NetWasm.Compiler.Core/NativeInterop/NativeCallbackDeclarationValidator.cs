using System;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed class NativeCallbackDeclarationValidator :
    INativeCallbackDeclarationValidator
{
    public void ValidateAddressTarget(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var definition = method.Definition;
        var declaration = definition.NativeCallback ?? throw Unsupported(
            method,
            "The method is not an unmanaged callback declaration.");
        if (!definition.IsStatic || definition.IsAbstract || !definition.HasBody ||
            definition.GenericArity != 0 ||
            method.DeclaringType.ContainsGenericParameters ||
            method.DeclaringType.Shape == CliTypeShape.GenericInstantiation ||
            !method.MethodArguments.IsDefaultOrEmpty)
        {
            throw Unsupported(
                method,
                "Native callbacks must be non-generic static methods on non-generic types.");
        }
        if (definition.NativeImport is not null || definition.JSImport is not null ||
            definition.JSExport is not null || definition.WitImport is not null ||
            definition.WitExport is not null || definition.WitPostReturn is not null)
        {
            throw Unsupported(
                method,
                "A native callback cannot also declare another interop boundary.");
        }
        if (declaration.IsVarArg || declaration.HasUnsupportedNamedArguments)
        {
            throw Unsupported(
                method,
                "A native callback is a fixed-arity method whose [UnmanagedCallersOnly] names only CallConvs and EntryPoint.");
        }
        if (UnmanagedCallingConventions.Unsupported(declaration.CallingConventions) is { } unsupported)
        {
            throw Unsupported(
                method,
                $"The native callback's {unsupported}; a callback is called with the C ABI: the default, " +
                "Cdecl, Stdcall or Thiscall, with SuppressGCTransition or MemberFunction.");
        }
        if (declaration.EntryPoint is { } entryPoint &&
            (string.IsNullOrWhiteSpace(entryPoint) ||
             entryPoint.IndexOfAny(['\0', '\r', '\n']) >= 0))
        {
            throw Unsupported(
                method,
                "A named native callback entry requires a non-empty native symbol without control characters.");
        }
    }

    private static CompilerException Unsupported(
        MethodInstanceModel method,
        string message) =>
        new(new(DiagnosticCode.NativeInterop, message, method.CanonicalName));
}
