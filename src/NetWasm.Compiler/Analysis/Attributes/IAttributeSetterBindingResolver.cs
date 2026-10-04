using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed record AttributeSetterBinding(
    MethodInstanceModel DelegateConstructor,
    MethodInstanceModel Invoke);

internal interface IAttributeSetterBindingResolver
{
    AttributeSetterBinding Resolve(CliTypeIdentity valueType);
}
