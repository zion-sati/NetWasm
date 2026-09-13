using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeTestPlanner(
    ITypeOperandResolver typeOperands,
    ITypeRelationshipClassifier relationships) : ITypeTestPlanner
{
    public ImmutableDictionary<string, TypeTestSiteModel> Build(
        IEnumerable<ManagedMethodBody> methods,
        IEnumerable<CliTypeIdentity> allocatedTypes) =>
        methods
            .SelectMany(method => method.Body.Instructions
                .Where(instruction => instruction.Operation is
                    CilOperation.CastClass or
                    CilOperation.IsInstance or
                    CilOperation.UnboxAny)
                .Select(instruction =>
                {
                    var target = typeOperands.Resolve(instruction, method.Method);
                    return (Method: method.Method, Instruction: instruction, Target: target);
                }))
            .Where(candidate =>
                candidate.Instruction.Operation != CilOperation.UnboxAny ||
                !candidate.Target.IsValueType)
            .Select(candidate => new TypeTestSiteModel(
                candidate.Method.CanonicalName,
                candidate.Instruction.Offset,
                candidate.Target,
                allocatedTypes
                    .Where(allocatedType => relationships
                        .Classify(allocatedType, candidate.Target)
                        .IsAssignmentCompatible)
                    .OrderBy(allocatedType => allocatedType.CanonicalName, StringComparer.Ordinal)
                    .ToImmutableArray()))
            .ToImmutableDictionary(site => site.Key, StringComparer.Ordinal);
}
