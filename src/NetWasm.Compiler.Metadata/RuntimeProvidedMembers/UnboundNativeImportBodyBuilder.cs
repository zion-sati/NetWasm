using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

// A P/Invoke whose library the program does not link (NativeLibraryBinding) throws DllNotFoundException when called,
// as .NET on Linux binds a P/Invoke when it first runs and fails then.
internal sealed class UnboundNativeImportBodyBuilder(
    ITypeFinderFactory types,
    MetadataCompilationSnapshot snapshot,
    IMethodRepository methods) : IUnboundNativeImportBodyBuilder
{
    // DllNotFoundException(string message).
    private static readonly CliTypeIdentity[] MessageParameter =
        [CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false)];

    private readonly Lazy<MethodInstanceModel> _constructor = new(() =>
    {
        var exception = types.Create(snapshot).FindType("System.DllNotFoundException");
        var constructor = exception.Methods.Select(methods.GetMethod)
            .Where(static method => method.Name == ".ctor")
            .Single(static method => method.Signature.ParameterSignatureTypes.SequenceEqual(MessageParameter));
        return new(constructor, CliTypeIdentity.FromDefinition(exception), [], constructor.Signature);
    });

    public CilMethodBody Build(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var import = method.Definition.UnboundNativeImport ?? throw new ArgumentException(
            "The method is not an unbound native import.", nameof(method));
        var message = $"Unable to load shared library '{import.LibraryName}' or one of its dependencies. " +
            $"The application does not link it, so '{import.EntryPoint}' cannot be called.";
        return new(method.Definition, 1, [],
        [
            new(0, 1, CilOperation.LoadString, new CilOperand.UserString(message)),
            new(1, 2, CilOperation.NewObject, new CilOperand.MethodInstance(_constructor.Value)),
            new(2, 3, CilOperation.Throw, new CilOperand.None()),
        ])
        {
            MethodInstance = method,
        };
    }
}
