using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeConstructionCilBuilder(IAttributeSetterBindingResolver setters) : IAttributeConstructionCilBuilder
{
    public AttributeCilFragment Build(AttributeConstructionPlan attribute, int firstLocal)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        ArgumentOutOfRangeException.ThrowIfNegative(firstLocal);
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
        var locals = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        var stack = 0;
        var maxStack = 0;

        foreach (var argument in attribute.ConstructorArguments)
        {
            EmitArgument(argument);
        }
        Emit(CilOperation.NewObject, new CilOperand.MethodInstance(attribute.Constructor),
            attribute.ConstructorArguments.Length, 1);
        var instance = AddLocal(attribute.AttributeType);
        Emit(CilOperation.StoreLocal, new CilOperand.Index(instance), 1, 0);
        foreach (var named in attribute.NamedArguments)
        {
            Emit(CilOperation.LoadLocal, new CilOperand.Index(instance), 0, 1);
            if (named.Field is not null && named.Setter is null)
            {
                EmitArgument(named.Value);
                Emit(CilOperation.StoreField, new CilOperand.FieldInstance(named.Field), 2, 0);
            }
            else if (named.Setter is not null && named.Field is null)
            {
                var binding = setters.Resolve(named.Value.Type);
                if (named.Setter.Definition.IsVirtual)
                {
                    Emit(CilOperation.Duplicate, new CilOperand.None(), 1, 2);
                    Emit(CilOperation.LoadVirtualFunction, new CilOperand.MethodInstance(named.Setter), 1, 1);
                }
                else
                {
                    Emit(CilOperation.LoadFunction, new CilOperand.MethodInstance(named.Setter), 0, 1);
                }
                Emit(CilOperation.NewObject, new CilOperand.MethodInstance(binding.DelegateConstructor), 2, 1);
                EmitArgument(named.Value);
                Emit(CilOperation.LoadString, new CilOperand.UserString(named.Name), 0, 1);
                Emit(CilOperation.Call, new CilOperand.MethodInstance(binding.Invoke), 3, 0);
            }
            else
            {
                throw Invalid($"named attribute argument '{named.Name}' must have one writable member");
            }
        }
        Emit(CilOperation.LoadLocal, new CilOperand.Index(instance), 0, 1);
        return new(instructions.ToImmutable(), locals.ToImmutable(), maxStack);

        int AddLocal(CliTypeIdentity type)
        {
            var index = firstLocal + locals.Count;
            locals.Add(type);
            return index;
        }

        void Emit(CilOperation operation, CilOperand operand, int consumed, int produced)
        {
            var offset = instructions.Count;
            instructions.Add(new(offset, offset + 1, operation, operand));
            stack += produced - consumed;
            maxStack = Math.Max(maxStack, stack);
        }

        void EmitArgument(AttributeArgumentPlan argument)
        {
            var serializedType = argument.SerializedType ?? argument.Type;
            switch (argument.Kind)
            {
                case AttributeArgumentKind.Null:
                    Emit(CilOperation.LoadNull, new CilOperand.None(), 0, 1);
                    return;
                case AttributeArgumentKind.Boolean:
                case AttributeArgumentKind.SignedInteger:
                    EmitInteger(serializedType, argument.SignedValue);
                    break;
                case AttributeArgumentKind.Character:
                case AttributeArgumentKind.UnsignedInteger:
                    EmitInteger(serializedType, unchecked((long)argument.UnsignedValue));
                    break;
                case AttributeArgumentKind.Floating32:
                    Emit(CilOperation.LoadFloat32,
                        new CilOperand.ConstantF4((float)argument.FloatingValue), 0, 1);
                    break;
                case AttributeArgumentKind.Floating64:
                    Emit(CilOperation.LoadFloat64,
                        new CilOperand.ConstantF8(argument.FloatingValue), 0, 1);
                    break;
                case AttributeArgumentKind.Text:
                    Emit(CilOperation.LoadString,
                        new CilOperand.UserString(argument.StringValue ??
                            throw Invalid("a non-null attribute string has no value")), 0, 1);
                    break;
                case AttributeArgumentKind.Type:
                    Emit(CilOperation.LoadTypeToken,
                        new CilOperand.TypeIdentity(argument.TypeValue ??
                            throw Invalid("an attribute Type argument has no identity")), 0, 1);
                    Emit(CilOperation.MaterializeType, new CilOperand.None(), 1, 1);
                    break;
                case AttributeArgumentKind.Array:
                    EmitArray(argument, serializedType);
                    break;
                default:
                    throw Invalid($"unsupported attribute argument kind '{argument.Kind}'");
            }
            if (argument.Type.StackKind == CliValueKind.ManagedReference &&
                serializedType.IsValueType)
            {
                Emit(CilOperation.Box, new CilOperand.TypeIdentity(serializedType), 1, 1);
            }
        }

        void EmitInteger(CliTypeIdentity type, long value)
        {
            if (type.StackKind == CliValueKind.I8)
            {
                Emit(CilOperation.LoadInt64, new CilOperand.ConstantI8(value), 0, 1);
            }
            else
            {
                Emit(CilOperation.LoadInt32, new CilOperand.ConstantI4(unchecked((int)value)), 0, 1);
            }
        }

        void EmitArray(AttributeArgumentPlan argument, CliTypeIdentity arrayType)
        {
            if (arrayType.Shape != CliTypeShape.SzArray)
            {
                throw Invalid("an attribute array argument must have a vector type");
            }
            var elementType = arrayType.ElementType!;
            Emit(CilOperation.LoadInt32, new CilOperand.ConstantI4(argument.Elements.Length), 0, 1);
            Emit(CilOperation.NewArray, new CilOperand.TypeIdentity(elementType), 1, 1);
            var array = AddLocal(arrayType);
            Emit(CilOperation.StoreLocal, new CilOperand.Index(array), 1, 0);
            for (var index = 0; index < argument.Elements.Length; index++)
            {
                Emit(CilOperation.LoadLocal, new CilOperand.Index(array), 0, 1);
                Emit(CilOperation.LoadInt32, new CilOperand.ConstantI4(index), 0, 1);
                EmitArgument(argument.Elements[index]);
                Emit(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(elementType), 3, 0);
            }
            Emit(CilOperation.LoadLocal, new CilOperand.Index(array), 0, 1);
        }
    }

    private static CompilerException Invalid(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.RuntimeContract, message));
}
