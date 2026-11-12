using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// The static fields that may hold something other than their default: every field reachable code
// stores to or takes the address of, and every field the host or reflection can reach. An address
// taken only to be dropped, as a fold leaves to keep its owner's initialization, writes nothing.
internal sealed class StaticFieldWriteCollector(IFieldRepository fields) : IStaticFieldWriteCollector
{
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public ImmutableHashSet<EntityKey> Collect(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var written = roots.Fields.ToImmutableHashSet().ToBuilder();
        written.UnionWith(program.FieldDescriptors.Values.Select(field => field.Definition.Key));
        foreach (var body in program.Methods.Values.Concat(program.ConstructedMethods.Values))
        {
            var instructions = body.Body.Instructions;
            for (var index = 0; index < instructions.Length; index++)
            {
                var instruction = instructions[index];
                // Valid IL never ends a method on an address, so one always follows.
                if (instruction.Operation == CilOperation.StoreStaticField ||
                    instruction.Operation == CilOperation.LoadStaticFieldAddress &&
                    instructions[index + 1].Operation != CilOperation.Pop)
                {
                    written.Add(_fields.GetField(CilOperands.FieldKey(instruction.Operand)).Key);
                }
            }
        }
        return written.ToImmutable();
    }
}
