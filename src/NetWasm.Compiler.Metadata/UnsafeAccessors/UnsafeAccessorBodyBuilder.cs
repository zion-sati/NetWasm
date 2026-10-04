using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

/// <summary>Builds ordinary CIL so accessors use existing dispatch, initialization and GC analysis.</summary>
internal sealed class UnsafeAccessorBodyBuilder : IUnsafeAccessorBodyBuilder
{
    public CilMethodBody Build(MethodInstanceModel accessor, UnsafeAccessorBinding binding)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(binding);
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
        var maxStack = 1;
        switch (binding)
        {
            case UnsafeAccessorBinding.Constructor constructor:
                LoadArguments(0, accessor.Signature.ParameterTypes.Length);
                Emit(CilOperation.NewObject, new CilOperand.MethodInstance(constructor.Target));
                break;
            case UnsafeAccessorBinding.Method method:
                // Static accessors carry an owner parameter for binding only. Instance
                // accessors use callvirt even for nonvirtual targets, preserving null checks.
                LoadArguments(method.Target.Definition.IsStatic ? 1 : 0,
                    accessor.Signature.ParameterTypes.Length);
                Emit(method.Target.Definition.IsStatic ? CilOperation.Call : CilOperation.CallVirtual,
                    new CilOperand.MethodInstance(method.Target));
                break;
            case UnsafeAccessorBinding.Field field:
                if (!field.Target.Definition.IsStatic)
                {
                    LoadArguments(0, 1);
                }

                Emit(field.Target.Definition.IsStatic
                        ? CilOperation.LoadStaticFieldAddress
                        : CilOperation.LoadFieldAddress,
                    new CilOperand.FieldInstance(field.Target));
                break;
            case UnsafeAccessorBinding.Failure failure:
                Emit(CilOperation.NewObject, new CilOperand.MethodInstance(failure.ExceptionConstructor));
                Emit(CilOperation.Throw, new CilOperand.None());
                return Body();
        }

        Emit(CilOperation.Return, new CilOperand.None());
        return Body();

        void LoadArguments(int first, int end)
        {
            maxStack = Math.Max(maxStack, end - first);
            for (var index = first; index < end; index++)
            {
                Emit(CilOperation.LoadArgument, new CilOperand.Index(index));
            }
        }

        void Emit(CilOperation operation, CilOperand operand) =>
            instructions.Add(new CilInstruction(instructions.Count, instructions.Count + 1, operation, operand));

        CilMethodBody Body() => new(accessor.Definition, maxStack, [], instructions.ToImmutable())
        {
            MethodInstance = accessor,
        };
    }
}
