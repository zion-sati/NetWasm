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
        if (import.HasWasmImportLinkage && IsReservedImportModule(import.LibraryName))
        {
            throw Unsupported(method,
                "A [WasmImportLinkage] library name cannot be a NetWasm or WASI import module.");
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
        if (import.IsVarArg || import.HasMarshalling)
        {
            throw Unsupported(method, "Native varargs and runtime marshalling are unsupported.");
        }
        // Winapi, Cdecl, StdCall and ThisCall are each the C ABI, as on desktop .NET's 64-bit
        // platforms (UnmanagedCallingConventions), which fail FastCall. A suppressed GC transition
        // is a promise the call may ignore, so the call keeps its own.
        var unsupported = import.UnsupportedCallingConvention ??
            (import.Attributes & MethodImportAttributes.CallingConventionMask) switch
            {
                MethodImportAttributes.CallingConventionWinApi or
                    MethodImportAttributes.CallingConventionCDecl or
                    MethodImportAttributes.CallingConventionStdCall or
                    MethodImportAttributes.CallingConventionThisCall => null,
                var convention => $"calling convention '{convention}' is not supported",
            };
        if (unsupported is not null)
        {
            throw Unsupported(method,
                $"The native import's {unsupported}; a native import is called with the C ABI: " +
                "Winapi, Cdecl, StdCall or ThisCall, with SuppressGCTransition or MemberFunction.");
        }
        if ((import.Attributes & ~(MethodImportAttributes.CallingConventionMask |
            MethodImportAttributes.ExactSpelling)) != 0)
        {
            throw Unsupported(method,
                "Only the C ABI with no additional native import semantics is supported.");
        }
    }

    // NetWasm's own modules and WASI interfaces are supplied by the runtime and
    // the component model, never by an application import.
    public static bool IsReservedImportModule(string module) =>
        module.StartsWith("netwasm.", StringComparison.Ordinal) ||
        module.StartsWith("netwasm:", StringComparison.Ordinal) ||
        module.StartsWith("wasi:", StringComparison.Ordinal) ||
        module.StartsWith("wasi_", StringComparison.Ordinal);

    private static CompilerException Unsupported(MethodInstanceModel method, string message) =>
        new(new(DiagnosticCode.NativeInterop, message, method.CanonicalName));
}
