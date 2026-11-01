using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReadOnlySpanDataResolver : IReadOnlySpanDataResolver
{
    public CilOperand.ReadOnlySpanData? Resolve(
        MethodInstanceModel? target,
        FieldInstanceModel field)
    {
        if (target is null)
        {
            return null;
        }

        var coreAssembly = target.Definition.Key.Assembly;
        if (!coreAssembly.Equals(target.DeclaringType.Assembly))
        {
            return null;
        }
        if (target.DeclaringType.FullName !=
            "System.Runtime.CompilerServices.RuntimeHelpers")
        {
            return null;
        }
        if (target.Definition.Name != "CreateSpan")
        {
            return null;
        }
        if (!target.Definition.IsStatic)
        {
            return null;
        }
        if (!target.Definition.IsPublic)
        {
            return null;
        }
        if (target.Definition.GenericArity != 1)
        {
            return null;
        }
        if (target.MethodArguments.Length != 1)
        {
            return null;
        }
        var elementType = target.MethodArguments[0];
        if (elementType.ContainsGenericParameters)
        {
            return null;
        }
        if (target.Signature.ParameterSignatureTypes.Length != 1)
        {
            return null;
        }
        var handleType = target.Signature.ParameterSignatureTypes[0];
        if (!coreAssembly.Equals(handleType.Assembly))
        {
            return null;
        }
        if (handleType.FullName != "System.RuntimeFieldHandle")
        {
            return null;
        }
        var returnType = target.Signature.ReturnSignatureType;
        if (!TryGetReadOnlySpanElement(returnType, out var returnElement))
        {
            return null;
        }
        if (!coreAssembly.Equals(returnType.ElementType!.Assembly))
        {
            return null;
        }
        if (!elementType.Equals(returnElement))
        {
            return null;
        }
        if (!TryGetPrimitiveSize(elementType, out var elementSize))
        {
            return null;
        }
        if (!field.Definition.IsStatic)
        {
            return null;
        }
        if (field.Definition.InitialData.IsDefaultOrEmpty)
        {
            return null;
        }
        if (field.Definition.InitialData.Length % elementSize != 0)
        {
            return null;
        }

        return new(
            field,
            returnType,
            elementType,
            field.Definition.InitialData.Length / elementSize);
    }

    internal static bool TryGetReadOnlySpanElement(
        CliTypeIdentity type,
        out CliTypeIdentity elementType)
    {
        if (type.Shape == CliTypeShape.GenericInstantiation &&
            type.ElementType!.FullName == "System.ReadOnlySpan`1" &&
            type.TypeArguments.Length == 1)
        {
            elementType = type.TypeArguments[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    internal static bool TryGetPrimitiveSize(CliTypeIdentity type, out int size)
    {
        size = type.CanonicalName switch
        {
            "primitive:bool" or "primitive:i1" or "primitive:u1" => 1,
            "primitive:char" or "primitive:i2" or "primitive:u2" => 2,
            "primitive:i4" or "primitive:u4" or "primitive:f4" => 4,
            "primitive:i8" or "primitive:u8" or "primitive:f8" => 8,
            _ => 0,
        };
        return size != 0;
    }
}
