using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// The CoreLib calls the decoder lowers the typed-reference instructions to, as NativeAOT lowers them to
// TypedReference's own helpers: the rest of the compiler sees ordinary calls.
internal sealed class TypedReferenceCallResolver(
    ITypeFinderFactory types,
    MetadataCompilationSnapshot snapshot,
    IMethodRepository methods,
    IMetadataStackTypeResolver stackTypes) : ITypedReferenceCallResolver
{
    private readonly Lazy<TypeDefinitionModel> _type = new(() => types.Create(snapshot).FindType("System.TypedReference"));

    public MethodInstanceModel Resolve(TypedReferenceInstruction instruction, ImmutableArray<CliTypeIdentity> methodArguments)
    {
        var name = instruction switch
        {
            TypedReferenceInstruction.MakeReference => "NetWasmMake",
            TypedReferenceInstruction.ReferenceValue => "NetWasmValue",
            TypedReferenceInstruction.ReferenceType => "NetWasmTargetType",
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        var definition = _type.Value.Methods.Select(methods.GetMethod).Single(method => method.Name == name);
        return new MethodInstanceModel(
            definition,
            CliTypeIdentity.FromDefinition(_type.Value),
            methodArguments,
            stackTypes.Resolve(definition.Signature, new CliGenericContext([], methodArguments)));
    }
}
