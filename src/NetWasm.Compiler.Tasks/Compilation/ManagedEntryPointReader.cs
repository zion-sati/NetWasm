using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed class ManagedEntryPointReader : IManagedEntryPointReader
{
    public ManagedEntryPoint Read(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        try
        {
            return ReadPortableExecutable(assemblyPath);
        }
        catch (BadImageFormatException)
        {
            throw InvalidEntryPoint(assemblyPath);
        }
    }

    private static ManagedEntryPoint ReadPortableExecutable(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.CorHeader ?? throw InvalidEntryPoint(assemblyPath);
        if (header.Flags.HasFlag(CorFlags.NativeEntryPoint) ||
            header.EntryPointTokenOrRelativeVirtualAddress == 0)
        {
            throw InvalidEntryPoint(assemblyPath);
        }

        var handle = MetadataTokens.EntityHandle(
            header.EntryPointTokenOrRelativeVirtualAddress);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            throw InvalidEntryPoint(assemblyPath);
        }

        var metadata = pe.GetMetadataReader();
        var methodHandle = (MethodDefinitionHandle)handle;
        var method = metadata.GetMethodDefinition(methodHandle);
        var synchronousAbi = ReadAbi(metadata, method, assemblyPath);
        var asyncEntryPoint = TryReadAsyncEntryPoint(
            metadata,
            method,
            synchronousAbi,
            assemblyPath);
        if (asyncEntryPoint is not null)
        {
            return asyncEntryPoint;
        }

        return new(
            ReadTypeName(metadata, method.GetDeclaringType()),
            metadata.GetString(method.Name),
            MetadataTokens.GetToken(methodHandle),
            synchronousAbi);
    }

    private static ManagedEntryPoint? TryReadAsyncEntryPoint(
        MetadataReader metadata,
        MethodDefinition selectedMethod,
        ManagedExecutableEntryPointAbi selectedAbi,
        string assemblyPath)
    {
        if (metadata.GetString(selectedMethod.Name) != "<Main>")
        {
            return null;
        }

        var candidates = metadata.GetTypeDefinition(selectedMethod.GetDeclaringType())
            .GetMethods()
            .Select(handle => (Handle: handle, Method: metadata.GetMethodDefinition(handle)))
            .Where(candidate => metadata.GetString(candidate.Method.Name) == "Main")
            .Select(candidate => TryReadAsyncCandidate(
                metadata,
                candidate.Handle,
                candidate.Method,
                selectedAbi,
                assemblyPath))
            .Where(candidate => candidate is not null)
            .ToArray();
        return candidates.SingleOrDefault();
    }

    private static ManagedEntryPoint? TryReadAsyncCandidate(
        MetadataReader metadata,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        ManagedExecutableEntryPointAbi selectedAbi,
        string assemblyPath)
    {
        if (!method.Attributes.HasFlag(MethodAttributes.Static))
        {
            return null;
        }

        var signature = metadata.GetBlobReader(method.Signature);
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            return null;
        }

        var parameterCount = signature.ReadCompressedInteger();
        var returnShape = ReadAsyncReturnShape(metadata, ref signature);
        if (returnShape is null)
        {
            return null;
        }

        var parameterShape = TryReadParameterShape(parameterCount, ref signature);
        if (parameterShape is null ||
            parameterShape != selectedAbi.ParameterShape ||
            returnShape != selectedAbi.ReturnShape)
        {
            return null;
        }

        return new(
            ReadTypeName(metadata, method.GetDeclaringType()),
            metadata.GetString(method.Name),
            MetadataTokens.GetToken(methodHandle),
            new(
                parameterShape.Value,
                returnShape.Value,
                ManagedExecutableCompletionShape.Asynchronous));
    }

    private static ManagedExecutableEntryPointAbi ReadAbi(
        MetadataReader metadata,
        MethodDefinition method,
        string assemblyPath)
    {
        if (!method.Attributes.HasFlag(MethodAttributes.Static))
        {
            throw InvalidEntryPoint(assemblyPath);
        }

        var signature = metadata.GetBlobReader(method.Signature);
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            throw InvalidEntryPoint(assemblyPath);
        }

        var parameterCount = signature.ReadCompressedInteger();
        var returnShape = signature.ReadSignatureTypeCode() switch
        {
            SignatureTypeCode.Void => ManagedExecutableReturnShape.Void,
            SignatureTypeCode.Int32 => ManagedExecutableReturnShape.ExitCode,
            _ => throw InvalidEntryPoint(assemblyPath),
        };
        var parameterShape = TryReadParameterShape(parameterCount, ref signature) ??
            throw InvalidEntryPoint(assemblyPath);
        return new(parameterShape, returnShape);
    }

    private static ManagedExecutableParameterShape? TryReadParameterShape(
        int parameterCount,
        ref BlobReader signature) => parameterCount switch
        {
            0 => ManagedExecutableParameterShape.None,
            1 when IsStringArray(ref signature) =>
                ManagedExecutableParameterShape.StringArray,
            _ => null,
        };

    private static ManagedExecutableReturnShape? ReadAsyncReturnShape(
        MetadataReader metadata,
        ref BlobReader signature)
    {
        var code = signature.ReadSignatureTypeCode();
        if (code == SignatureTypeCode.TypeHandle)
        {
            return IsType(
                metadata,
                signature.ReadTypeHandle(),
                "System.Threading.Tasks",
                "Task")
                    ? ManagedExecutableReturnShape.Void
                    : null;
        }

        if (code != SignatureTypeCode.GenericTypeInstance ||
            signature.ReadSignatureTypeCode() != SignatureTypeCode.TypeHandle ||
            !IsType(
                metadata,
                signature.ReadTypeHandle(),
                "System.Threading.Tasks",
                "Task`1") ||
            signature.ReadCompressedInteger() != 1 ||
            signature.ReadSignatureTypeCode() != SignatureTypeCode.Int32)
        {
            return null;
        }

        return ManagedExecutableReturnShape.ExitCode;
    }

    private static bool IsType(
        MetadataReader metadata,
        EntityHandle handle,
        string @namespace,
        string name)
    {
        var identity = handle.Kind == HandleKind.TypeDefinition
            ? ReadDefinitionIdentity(metadata.GetTypeDefinition(
                (TypeDefinitionHandle)handle))
            : ReadReferenceIdentity(metadata.GetTypeReference(
                (TypeReferenceHandle)handle));
        return metadata.GetString(identity.Namespace) == @namespace &&
               metadata.GetString(identity.Name) == name;

        static (StringHandle Namespace, StringHandle Name) ReadDefinitionIdentity(
            TypeDefinition definition) => (definition.Namespace, definition.Name);

        static (StringHandle Namespace, StringHandle Name) ReadReferenceIdentity(
            TypeReference reference) => (reference.Namespace, reference.Name);
    }

    private static bool IsStringArray(ref BlobReader signature) =>
        signature.ReadSignatureTypeCode() == SignatureTypeCode.SZArray &&
        signature.ReadSignatureTypeCode() == SignatureTypeCode.String;

    private static string ReadTypeName(
        MetadataReader metadata,
        TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        var declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            return $"{ReadTypeName(metadata, declaringType)}+{name}";
        }

        var @namespace = metadata.GetString(type.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
    }

    private static CompilerException InvalidEntryPoint(string assemblyPath) => new(
        new CompilerDiagnostic(
            DiagnosticCode.InvalidEntryPoint,
            $"assembly '{Path.GetFileName(assemblyPath)}' does not contain a managed executable entry point"));
}
