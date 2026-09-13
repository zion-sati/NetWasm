using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumStorageResolver(
    ITypeRepository types,
    IFieldRepository fields,
    ITargetLayout layouts,
    IValueLayoutProvider values,
    ITypeDescriptorSource descriptors) : IEnumStorageResolver
{
    public ImmutableArray<EnumStorage> Resolve() =>
        [.. descriptors.TypeDescriptors
            .Where(descriptor => IsEnumType(types.GetTypeDefinition(descriptor.Type)))
            .Select(CreateStorage)];

    private EnumStorage CreateStorage(TypeDescriptorLayout descriptor)
    {
        var type = types.GetTypeDefinition(descriptor.Type);
        var field = type.Fields
            .Select(fields.GetField)
            .Single(candidate => candidate.Name == "value__");
        var layout = values.GetValueLayout(field.SignatureType);
        return new EnumStorage(
            descriptor,
            CliTypeIdentity.Named(
                descriptor.Type.Assembly,
                type.Namespace,
                type.Name,
                isValueType: true),
            field.SignatureType,
            layout,
            WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, layout.Alignment));
    }

    private static bool IsEnumType(TypeDefinitionModel type) => type.IsEnum;
}
