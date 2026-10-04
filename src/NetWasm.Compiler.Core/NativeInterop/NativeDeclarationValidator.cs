using System;
using System.Reflection;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed class NativeDeclarationValidator : INativeDeclarationValidator
{
    public void Validate(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var definition = method.Definition;
        var import = definition.NativeImport ?? throw Unsupported(method,
            "The method is not a static native declaration.");
        if (string.IsNullOrWhiteSpace(import.LibraryName) ||
            string.IsNullOrWhiteSpace(import.EntryPoint) ||
            import.LibraryName.Contains('\0') || import.EntryPoint.Contains('\0'))
        {
            throw Unsupported(method, "A native library and entry symbol must be explicitly named.");
        }
        if (!definition.IsStatic || definition.IsAbstract || definition.HasBody ||
            definition.GenericArity != 0 || method.DeclaringType.ContainsGenericParameters ||
            method.DeclaringType.Shape == CliTypeShape.GenericInstantiation ||
            !method.MethodArguments.IsDefaultOrEmpty)
        {
            throw Unsupported(method, "Native declarations must be non-generic static extern methods.");
        }
        if (definition.JSImport is not null || definition.JSExport is not null ||
            definition.WitImport is not null || definition.WitExport is not null ||
            definition.WitPostReturn is not null)
        {
            throw Unsupported(method, "A native declaration cannot also be a host or component boundary.");
        }
        if (import.IsVarArg || import.HasMarshalling || import.SuppressesGcTransition ||
            import.HasCustomCallingConvention)
        {
            throw Unsupported(method,
                "Native varargs, runtime marshalling, custom calling conventions and suppressed GC transitions are unsupported.");
        }
        var convention = import.Attributes & MethodImportAttributes.CallingConventionMask;
        var supportedFlags = MethodImportAttributes.CallingConventionMask |
            MethodImportAttributes.ExactSpelling;
        if (convention is not (MethodImportAttributes.CallingConventionWinApi or
            MethodImportAttributes.CallingConventionCDecl) ||
            (import.Attributes & ~supportedFlags) != 0)
        {
            throw Unsupported(method,
                "Only the default C ABI or Cdecl with no additional native import semantics is supported.");
        }
    }

    private static CompilerException Unsupported(MethodInstanceModel method, string message) =>
        new(new(DiagnosticCode.NativeInterop, message, method.CanonicalName));
}
