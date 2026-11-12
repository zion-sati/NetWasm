using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// A static field no reachable code writes holds its default, so its loads read that default and
// the code their value decides folds: an Environment.ExitCode nothing sets leaves a void Main's
// process exit to the success path, as an int Main returning 0 does. A field with initial data, a
// literal and a volatile load keep their loads. Where the owner has a static constructor, the load
// still runs it, through the field's address, as .NET's first access would.
internal sealed class UnwrittenStaticFieldProgramReducer(
    IStaticFieldWriteCollector writes,
    IFieldRepository fields,
    ITypeIdentityResolver typeIdentities,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodSpecializer rewrites) : IReachableProgramReducer
{
    private readonly IStaticFieldWriteCollector _writes = writes ?? throw new ArgumentNullException(nameof(writes));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ?? throw new ArgumentNullException(nameof(typeIdentities));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IMethodSpecializer _rewrites = rewrites ?? throw new ArgumentNullException(nameof(rewrites));

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var written = _writes.Collect(program, roots);
        var initializers = new Dictionary<EntityKey, bool>();
        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var method in program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .DistinctBy(body => body.Method.CanonicalName))
        {
            var instructions = method.Body.Instructions;
            List<CilInstruction>[]? replacements = null;
            for (var index = 0; index < instructions.Length; index++)
            {
                var instruction = instructions[index];
                // A volatile load keeps its memory semantics.
                if (instruction.Operation != CilOperation.LoadStaticField ||
                    index > 0 && instructions[index - 1].Operation == CilOperation.Volatile)
                {
                    continue;
                }
                var field = _fields.GetField(CilOperands.FieldKey(instruction.Operand));
                if (written.Contains(field.Key) || Default(field) is not { } constant) continue;
                replacements ??= [.. instructions.Select(original => new List<CilInstruction> { original })];
                replacements[index] = Initializes(field.DeclaringType, initializers)
                    ? [instruction with { Operation = CilOperation.LoadStaticFieldAddress },
                        StaticFieldConstantLoads.Instruction(CilOperation.Pop, new CilOperand.None()), constant]
                    : [constant];
            }
            if (replacements is not null)
            {
                result.Add(method.Method.CanonicalName, _rewrites.Rewrite(RenumberedInstructions.Splice(method.Body, replacements)));
            }
        }
        return result.ToImmutable();
    }

    // The load of the default a static field holds, or null where it may hold other data or its
    // type has no constant load.
    private CilInstruction? Default(FieldDefinitionModel field)
    {
        if (!field.IsStatic || field.IsLiteral || !field.InitialData.IsDefaultOrEmpty ||
            StaticFieldConstantLoads.Storage(field.SignatureType, _typeDefinitions) is not { } storage)
        {
            return null;
        }
        var (reference, type) = storage;
        return StaticFieldConstantLoads.Load(reference, type, StaticFieldConstantLoads.StackKind(reference, type), 0, 0);
    }

    // Whether the field's owner has a static constructor its first access runs.
    private bool Initializes(EntityKey declaringType, Dictionary<EntityKey, bool> initializers)
    {
        if (!initializers.TryGetValue(declaringType, out var initializes))
        {
            var owner = _typeDefinitions.ResolveTypeIdentity(_typeIdentities.GetTypeIdentity(declaringType));
            initializes = owner.Methods.Select(_methods.GetMethod).Any(method => method.Name == ".cctor");
            initializers.Add(declaringType, initializes);
        }
        return initializes;
    }
}
