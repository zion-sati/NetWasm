using System;
using System.IO;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel;

public interface IPlainCoreModuleImportValidator
{
    void Validate(string modulePath);
}

// A plain core module imports only its world's functions, under the Component Model's standard
// core names (cm32p2|interface or cm64p2|interface): no WASI interface, no Component Model
// built-in and nothing of NetWasm's own.
public sealed class PlainCoreModuleImportValidator : IPlainCoreModuleImportValidator
{
    public void Validate(string modulePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        if (!WasmBinaryImportSignatureReader.TryRead(File.ReadAllBytes(modulePath), out var imports))
        {
            throw ComponentException.Invalid("a plain core module's imports must be functions of scalar core types");
        }
        var forbidden = imports
            .Select(import => import.Identity)
            .FirstOrDefault(identity => !IsWorldFunction(identity.Module));
        if (forbidden is not null)
        {
            throw ComponentException.Invalid(
                $"a plain core module imports only its world's functions, but it imports '{forbidden.Module}#{forbidden.Name}'");
        }
    }

    private static bool IsWorldFunction(string module)
    {
        var separator = module.IndexOf('|', StringComparison.Ordinal);
        return separator > 0 &&
            module[..separator] is "cm32p2" or "cm64p2" &&
            !module.AsSpan(separator + 1).StartsWith("wasi:", StringComparison.Ordinal);
    }
}
